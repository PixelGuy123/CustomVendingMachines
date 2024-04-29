using BepInEx;
using HarmonyLib;
using MTM101BaldAPI;
using MTM101BaldAPI.AssetTools;
using MTM101BaldAPI.Registers;
using PixelInternalAPI.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace CustomVendingMachines
{
	[BepInPlugin("pixelguy.pixelmodding.baldiplus.customvendingmachines", PluginInfo.PLUGIN_NAME, "1.0.0")]
	[BepInDependency("mtm101.rulerp.bbplus.baldidevapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("pixelguy.pixelmodding.baldiplus.pixelinternalapi", BepInDependency.DependencyFlags.HardDependency)]
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
					Debug.LogException(e);
					Debug.LogWarning("BBCustomVendingMachines: Failed to load json!: " + Path.GetFileName(file));
				}
			}
		}
		private void Awake()
		{

			Harmony h = new("pixelguy.pixelmodding.baldiplus.customvendingmachines");
			h.PatchAll();

			AddDataFromDirectory(AssetLoader.GetModPath(this)); // Read from the directory already

			LoadingEvents.RegisterOnAssetsLoaded(() =>
			{
				try
				{
					foreach (var data in datas)
					{
						Texture2D normaltex;
						Texture2D outTex = null;
						try
						{
							EnumExtensions.GetFromExtendedName<Items>(data.Value.itemName);

							if (data.Value.usesLeft == 0) // Lotta of checks lol
								throw new ArgumentException("the usesLeft being set to 0");
							if (data.Value.minAmount < 0)
								throw new ArgumentException("the minAmount being below 0");
							if (data.Value.minAmount > data.Value.maxAmount)
								throw new ArgumentException("the minAmount being higher than maxAmount");
							if (data.Value.sodaMachineWeight < 0)
								throw new ArgumentException("the sodaMachineWeight being below 0");

							if (!File.Exists(Path.Combine(data.Key, data.Value.normalTextureFileName)))
								throw new ArgumentException("a missing texture. Texture name: " + data.Value.normalTextureFileName);

							normaltex = AssetLoader.TextureFromFile(Path.Combine(data.Key, data.Value.normalTextureFileName));
							if (normaltex.width != 128 || normaltex.height != 224)
								throw new ArgumentException($"an invalid texture: {normaltex.width}/{normaltex.height} | Expected size: 128/224 >> Texture Name: {data.Value.normalTextureFileName}");

							if (data.Value.usesLeft > 0) // The out texture will be ignored if the vending machine has infinite uses (uses < 0 are considered infinite)
							{
								outTex = AssetLoader.TextureFromFile(Path.Combine(data.Key, data.Value.outOfStockFileName));
								if (outTex.width != 128 || outTex.height != 224)
									throw new ArgumentException($"an invalid texture: {outTex.width}/{outTex.height} | Expected size: 128/224 >> Texture Name: {data.Value.outOfStockFileName}");
							}
						}
						catch (ArgumentException e)
						{
							Debug.LogWarning($"BBCustomVendingMachines: Failed to load vending machine ({data.Value.itemName}) due to {e.Message}");
							continue;
						}
						catch
						{
							Debug.LogWarning($"BBCustomVendingMachines: Failed to load vending machine due to an invalid or inexistent Items enum: {data.Value.itemName}"); 
							continue;
						}

						var sodaMachine = ObjectCreationExtensions.CreateSodaMachineInstance(normaltex
							,outTex);
						// Include soda machine as a prefab of course, so it appears
						sodaMachine.name = $"{data.Value.itemName}SodaMachine";
						sodaMachine.gameObject.SetActive(false);
						data.Value.machine = sodaMachine;
						/*
						and you want to know the solution i came up with?
						setting the positon to 0,float.max,0
						- MissingTextureMan101
						 */

						var vendingMachineBuilder = new GameObject($"{data.Value.itemName}SodaMachineBuilder_{data.Value.normalTextureFileName}").AddComponent<GenericHallBuilder>();
						DontDestroyOnLoad(vendingMachineBuilder.gameObject);
						vendingMachineBuilder.gameObject.SetActive(false);

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
					}
				}
				catch (Exception e)
				{
					Debug.LogException(e);
					MTM101BaldiDevAPI.CauseCrash(Info, e);
				}
			}, false);


			GeneratorManagement.Register(this, GenerationModType.Addend, (name, num, ld) =>
			{
				bool endless = name == "INF";
				for (int i = num; !endless || i >= lastlevelnum; i--)
				{
					foreach (var machine in sodaMachines)
					{

						if (!machine.Value.IncludeInLevel(endless ? name + i.ToString() : name) || ld.specialHallBuilders.Contains(machine.Key)) continue;
						machine.Value.machine.SetPotentialItems(new WeightedItemObject() { selection = ItemMetaStorage.Instance.FindByEnum(EnumExtensions.GetFromExtendedName<Items>(machine.Value.itemName)).value, weight = 1 })
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

		internal static int lastlevelnum = 1;

		internal static List<WeightedObjectBuilder> builders = [];

		readonly internal List<KeyValuePair<WeightedObjectBuilder, VendingMachineData>> sodaMachines = [];

		readonly static List<KeyValuePair<string, VendingMachineData>> datas = [];

		readonly static internal List<GameObject> prefabs = [];
	}

	[Serializable]
	class VendingMachineData
	{
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

	[HarmonyPatch]
	class PrefabActivation
	{
		[HarmonyPatch(typeof(GameInitializer), "Initialize")]
		[HarmonyPostfix]
		static void ActivateThem(SceneObject ___sceneObject)
		{ 
			if (___sceneObject != null && ___sceneObject)
				CustomVendingMachinesPlugin.prefabs.ForEach(x => x.SetActive(true)); 
		}

		[HarmonyPatch(typeof(BaseGameManager), "Initialize")]
		[HarmonyPrefix]
		static void DisableThem() => CustomVendingMachinesPlugin.prefabs.ForEach(x => x.SetActive(false));
	}
}
