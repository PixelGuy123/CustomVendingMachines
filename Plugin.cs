using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using MTM101BaldAPI;
using MTM101BaldAPI.AssetTools;
using MTM101BaldAPI.Registers;
using PixelInternalAPI;
using PixelInternalAPI.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace CustomVendingMachines
{
	[BepInDependency("mtm101.rulerp.bbplus.baldidevapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("pixelguy.pixelmodding.baldiplus.pixelinternalapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInPlugin("pixelguy.pixelmodding.baldiplus.customvendingmachines", PluginInfo.PLUGIN_NAME, "1.0.3")]
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

			LoadingEvents.RegisterOnAssetsLoaded(Info, LoadVendingMachines(), false);


			GeneratorManagement.Register(this, GenerationModType.Addend, (name, num, ld) =>
			{
				ld.minSpecialBuilders += Mathf.Min(datas.Count, 3);
				ld.maxSpecialBuilders += Mathf.Min(datas.Count, 3);

				bool endless = name == "INF";
				for (int i = num; !endless || i >= lastlevelnum; i--)
				{
					foreach (var machine in sodaMachines)
					{
						if (!machine.Value.IncludeInLevel(endless ? name + i.ToString() : name) || ld.specialHallBuilders.Contains(machine.Key)) continue;

						machine.Value.machine.SetPotentialItems(new WeightedItemObject() { selection = 
							string.IsNullOrEmpty(machine.Value.modId) ? ItemMetaStorage.Instance.FindByEnum(machine.Value.en).value :
							ItemMetaStorage.Instance.FindByEnumFromMod(machine.Value.en, Chainloader.PluginInfos[machine.Value.modId]).value, weight = 1 })
						.SetUses(machine.Value.usesLeft);

						ld.specialHallBuilders = ld.specialHallBuilders.AddToArray(machine.Key);
						builders.Add(machine.Key);
					}
					if (!endless)
						break;
				}
				
				ld.MarkAsNeverUnload();
				if (endless)
				{ 
					lastlevelnum = num;
					foreach (var b in builders) // Fail safe afterwards
					{
						if (ld.specialHallBuilders.Contains(b)) continue;
						ld.specialHallBuilders = ld.specialHallBuilders.AddToArray(b);
					}
				}
			});

		}

		IEnumerator LoadVendingMachines()
		{

			yield return datas.Count;

			int sodaCount = GenericExtensions.FindResourceObjects<SodaMachine>().Length; // amount of soda machine prefabs

			foreach (var data in datas)
			{
				yield return "Loading vending machine for item: " + data.Value.itemName + " ...";
				Texture2D normaltex;
				Texture2D outTex = null;
				try
				{
					Items en = EnumExtensions.GetFromExtendedName<Items>(data.Value.itemName);
					BepInEx.PluginInfo inf = string.IsNullOrEmpty(data.Value.modId) || !Chainloader.PluginInfos.ContainsKey(data.Value.modId) ? null : Chainloader.PluginInfos[data.Value.modId];

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

				var vendingMachineBuilder = new GameObject($"{data.Value.itemName}SodaMachineBuilder_{data.Value.normalTextureFileName}").AddComponent<GenericHallBuilder>();

				ObjectBuilderMetaStorage.Instance.Add(new(Info, vendingMachineBuilder));

				vendingMachineBuilder.SetObjectPlacer(
					ObjectCreationExtensions.SetANewObjectPlacer(
						sodaMachine.gameObject,
						CellCoverage.North | CellCoverage.Down, TileShape.Closed, TileShape.Single, TileShape.Straight, TileShape.Corner, TileShape.End)
						.SetMinAndMaxObjects(data.Value.minAmount, data.Value.maxAmount)
						.SetTilePreferences(true, false, true)
					);

				sodaMachines.Add(new(new() { selection = vendingMachineBuilder, weight = data.Value.sodaMachineWeight }, data.Value));
				prefabs.Add(sodaMachine.gameObject);
				prefabs.Add(vendingMachineBuilder.gameObject);

				vendingMachineBuilder.gameObject.ConvertToPrefab(true);
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

			foreach (var mac in sodaMachines)
				mac.Key.weight /= sodaCount;

			yield break;
		}

		readonly static List<string> errors = [];

		internal static int lastlevelnum = 1;

		internal static List<WeightedObjectBuilder> builders = [];

		readonly internal List<KeyValuePair<WeightedObjectBuilder, VendingMachineData>> sodaMachines = [];

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
		private static void Prefix()
		{
			CustomVendingMachinesPlugin.lastlevelnum = 1;
			CustomVendingMachinesPlugin.builders.Clear();
		}
	}
}
