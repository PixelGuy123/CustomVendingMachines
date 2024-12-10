using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using MTM101BaldAPI;
using MTM101BaldAPI.AssetTools;
using MTM101BaldAPI.Registers;
using PixelInternalAPI;
using PixelInternalAPI.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CustomVendingMachines
{
	[BepInDependency("mtm101.rulerp.bbplus.baldidevapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("pixelguy.pixelmodding.baldiplus.pixelinternalapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInPlugin("pixelguy.pixelmodding.baldiplus.customvendingmachines", PluginInfo.PLUGIN_NAME, "1.0.4.2")]
	public class CustomVendingMachinesPlugin : BaseUnityPlugin
	{
		// *** Use this method for your mod to add custom vending machines ***
		public static void AddDataFromDirectory(string path)
		{
			if (!Directory.Exists(path))
			{
				Debug.LogWarning("BBCustomVendingMachines: the path to the directory doesn\'t exist: " + path);
				return;
			}

			foreach (var file in Directory.GetFiles(path))
			{
				if (Path.GetExtension(file) != ".json") continue;

				try
				{
					datas.Add(new(path, JsonUtility.FromJson<VendingMachineData>(File.ReadAllText(file))));
				}
				catch (Exception e)
				{
					Debug.LogWarning("BBCustomVendingMachines: Failed to load json!: " + Path.GetFileName(file));
					Debug.LogException(e);
					errors.Add("Failed to load json!: " + Path.GetFileName(file));
				}
			}
		}
		private void Awake()
		{
			Harmony h = new("pixelguy.pixelmodding.baldiplus.customvendingmachines");
			h.PatchAll();

			AddDataFromDirectory(AssetLoader.GetModPath(this)); // Read from the directory already

			GeneratorManagement.Register(this, GenerationModType.Addend, (name, num, sco) =>
			{
				var ld = sco.levelObject;
				if (ld == null)
					return;

				LoadVendingMachines();

				bool endless = name == "INF";
				for (int i = num; !endless || i >= lastlevelnum; i--)
				{
					foreach (var machine in sodaMachines)
					{
						if (!machine.IncludeInLevel(endless ? name + i.ToString() : name)) continue;
						ItemObject item = !Chainloader.PluginInfos.TryGetValue(machine.modId, out var inf) ? // If no Info found, findByNormalEnum
							ItemMetaStorage.Instance.FindByEnum(machine.en).value :
							ItemMetaStorage.Instance.FindByEnumFromMod(machine.en, inf).value;

						machine.machine.SetPotentialItems(new WeightedItemObject() { selection = item, weight = 1 })
						.SetUses(machine.usesLeft);


						StructureWithParameters param = ld.forcedStructures.FirstOrDefault(s => s.prefab is Structure_EnvironmentObjectPlacer);
						if (param == null)
							ld.potentialStructures.FirstOrDefault(s => s.selection.prefab is Structure_EnvironmentObjectPlacer);

						if (param == null) // If it's still null... then skip
							continue;


						param.parameters.prefab = param.parameters.prefab.AddToArray(new() { selection = machine.machine.gameObject, weight = machine.sodaMachineWeight });
						param.parameters.minMax[0].x += machine.minAmount;
						param.parameters.minMax[0].z += machine.maxAmount;
					}
					if (!endless)
						break;
				}
				
				ld.MarkAsNeverUnload();
				lastlevelnum = num;
			});

		}

		void LoadVendingMachines()
		{
			if (initializedMachines) return;
			initializedMachines = true;

			int sodaCount = GenericExtensions.FindResourceObjects<SodaMachine>().Length; // amount of soda machine prefabs

			foreach (var data in datas)
			{
				Texture2D normaltex;
				Texture2D outTex = null;
				try
				{
					Items en = EnumExtensions.GetFromExtendedName<Items>(data.Value.itemName);
					Chainloader.PluginInfos.TryGetValue(data.Value.modId, out BepInEx.PluginInfo inf);

					if (!ItemMetaStorage.Instance.All().Any(x => x.id == en && (inf == null || x.info == inf)))
						throw new InvalidItemsEnumException(data.Value.itemName);

					if (data.Value.usesLeft == 0) // Lotta of checks lol
						throw new ArgumentException("the usesLeft being set to 0");
					if (data.Value.minAmount < 0)
						throw new ArgumentException("the minAmount being below 0");
					if (data.Value.minAmount > data.Value.maxAmount)
						throw new ArgumentException("the minAmount being higher than maxAmount");
					if (data.Value.sodaMachineWeight < 0)
						throw new ArgumentException("the sodaMachineWeight being below 0");

					if (!File.Exists(Path.Combine(data.Key, data.Value.normalTextureFileName)))
						throw new ArgumentException("a missing normal texture. Texture name: " + data.Value.normalTextureFileName);

					normaltex = AssetLoader.TextureFromFile(Path.Combine(data.Key, data.Value.normalTextureFileName));
					if (normaltex.width != 128 || normaltex.height != 224)
						throw new ArgumentException($"an invalid texture: {normaltex.width}/{normaltex.height} | Expected size: 128/224 >> Texture Name: {data.Value.normalTextureFileName}");

					if (data.Value.usesLeft > 0) // The out texture will be ignored if the vending machine has infinite uses (uses < 0 are considered infinite)
					{
						if (!File.Exists(Path.Combine(data.Key, data.Value.outOfStockFileName)))
							throw new ArgumentException("a missing outOfStock texture. Texture name: " + data.Value.outOfStockFileName);

						outTex = AssetLoader.TextureFromFile(Path.Combine(data.Key, data.Value.outOfStockFileName));
						if (outTex.width != 128 || outTex.height != 224)
							throw new ArgumentException($"an invalid texture: {outTex.width}/{outTex.height} | Expected size: 128/224 >> Texture Name: {data.Value.outOfStockFileName}");
					}

					data.Value.en = en;
				}
				catch(ArgumentException e)
				{
					errors.Add($"Failed to load vending machine (\"{data.Value.itemName}\") due to {e.Message}");
					Debug.LogWarning($"BBCustomVendingMachines: Failed to load vending machine ({data.Value.itemName}) due to {e.Message}");
					continue;
				}
				catch (InvalidItemsEnumException e)
				{
					errors.Add($"Failed to load vending machine due to {e.Message}");
					Debug.LogWarning($"BBCustomVendingMachines: Failed to load vending machine due to {e.Message}");
					continue;
				}
				catch(Exception e)
				{
					errors.Add($"Failed to load vending machine due to a specific bug or inexistent Items enum: \"{data.Value.itemName}\"");
					Debug.LogWarning($"BBCustomVendingMachines: Failed to load vending machine due to a specific bug or inexistent Items enum: {data.Value.itemName}");
					Debug.LogException(e);
					continue;
				}

				var sodaMachine = ObjectCreationExtensions.CreateSodaMachineInstance(normaltex
					, outTex);
				// Include soda machine as a prefab of course, so it appears
				sodaMachine.name = $"{data.Value.itemName}SodaMachine";
				data.Value.machine = sodaMachine;

				//.Instance.Add(new(Info, vendingMachineBuilder)); Meta data for structure builders... if there was one lol

				sodaMachines.Add(data.Value);
				prefabs.Add(sodaMachine.gameObject);
				sodaCount++;
			}

			StringBuilder accerrors = new();
			for (int i = 0; i < errors.Count;)
			{
				accerrors.AppendLine(errors[i]);
				if (++i % 3 == 0)
				{
					ResourceManager.RaisePopup(Info, accerrors.ToString());
					accerrors.Clear();
				}
			}
			if (accerrors.Length > 0)
				ResourceManager.RaisePopup(Info, accerrors.ToString());

			// Balance out weights
			if (sodaCount != 0)
				sodaMachines.ForEach(mac => mac.sodaMachineWeight /= sodaCount);
		}

		static bool initializedMachines = false;

		readonly static List<string> errors = [];

		internal static int lastlevelnum = 1;

		readonly internal List<VendingMachineData> sodaMachines = [];

		readonly static List<KeyValuePair<string, VendingMachineData>> datas = [];

		readonly static internal List<GameObject> prefabs = [];
	}

	class InvalidItemsEnumException(string invalidEnumName) : Exception($"an Item that doesn\'t exist in the meta storage ({invalidEnumName})");

	[Serializable]
	class VendingMachineData
	{
		public string modId = string.Empty;
		public int minAmount = 1;
		public int maxAmount = 3;
		public int sodaMachineWeight = 100;
		public int usesLeft = 1;
		public string normalTextureFileName = string.Empty;
		public string outOfStockFileName = string.Empty;
		public string itemName = string.Empty;
		public string[] allowedLevels = [];
		public bool IncludeInLevel(string floor) => // A BUNCH OF CHECKS
			allowedLevels.Contains(floor);

		[NonSerialized]
		public SodaMachine machine;

		[NonSerialized]
		public Items en;
	}

	/*
	class ModdedSaveThingy : ModdedSaveGameIOText
	{
		public override BepInEx.PluginInfo pluginInfo => Plugin.i.Info;
		public override void LoadText(string toLoad) =>
			Plugin.queuedBuilders = toLoad;
		

		public override string SaveText()
		{
			StringBuilder bld = new();
			foreach (var b in Plugin.alreadyIncludedBuilders)
				bld.Append(b.selection.name + "/");
			return bld.ToString();
		}

		public override void Reset()
		{
		}
	}
	*/

	// When you quit the game, it should always lead to this screen anyways
	[HarmonyPatch(typeof(MainMenu), "Start")]
	class ResetIncludedBuildersPatch
	{
		private static void Prefix() =>
			CustomVendingMachinesPlugin.lastlevelnum = 1;
	}
}
