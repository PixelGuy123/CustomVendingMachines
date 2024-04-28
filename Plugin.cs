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

namespace CustomVendingMachines.Plugin
{
	[BepInPlugin("pixelguy.pixelmodding.baldiplus.customvendingmachines", PluginInfo.PLUGIN_NAME, "1.0.0")]
	[BepInDependency("mtm101.rulerp.bbplus.baldidevapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("pixelguy.pixelmodding.baldiplus.pixelinternalapi", BepInDependency.DependencyFlags.HardDependency)]
	public class Plugin : BaseUnityPlugin
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
						try
						{
							EnumExtensions.GetFromExtendedName<Items>(data.Value.itemName);
						}
						catch
						{
							Debug.LogWarning($"BBCustomVendingMachines: Failed to load vending machine due to an invalid or inexistent Items enum: {data.Value.itemName}"); 
							continue;
						}

						var sodaMachine = ObjectCreationExtensions.CreateSodaMachineInstance(
							AssetLoader.TextureFromFile(Path.Combine(data.Key, data.Value.normalTextureFileName)), AssetLoader.TextureFromFile(Path.Combine(data.Key, data.Value.outOfStockFileName)));
						// Include soda machine as a prefab of course, so it appears
						sodaMachine.gameObject.SetActive(true);
						sodaMachine.transform.position = Vector3.up * float.MaxValue;
						sodaMachine.name = $"{data.Value.itemName}SodaMachine";
						data.Value.machine = sodaMachine;
						/*
						and you want to know the solution i came up with?
						setting the positon to 0,float.max,0
						- MissingTextureMan101
						 */

						var vendingMachineBuilder = new GameObject($"{data.Value.itemName}SodaMachineBuilder_{data.Value.normalTextureFileName}").AddComponent<GenericHallBuilder>();
						DontDestroyOnLoad(vendingMachineBuilder.gameObject);

						ObjectBuilderMetaStorage.Instance.Add(new(Info, vendingMachineBuilder));

						vendingMachineBuilder.SetObjectPlacer(
							ObjectCreationExtensions.SetANewObjectPlacer(
								sodaMachine.gameObject,
								CellCoverage.North | CellCoverage.Down, TileShape.Closed, TileShape.Single, TileShape.Straight, TileShape.Corner, TileShape.End)
								.SetMinAndMaxObjects(data.Value.minAmount, data.Value.maxAmount)
								.SetTilePreferences(true, false, true)
							);

						sodaMachines.Add(new(new() { selection = vendingMachineBuilder, weight = data.Value.sodaMachineWeight }, data.Value));
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
						machine.Value.machine.SetPotentialItemsAndUses(machine.Value.usesLeft, new WeightedItemObject() { selection = ItemMetaStorage.Instance.FindByEnum(EnumExtensions.GetFromExtendedName<Items>(machine.Value.itemName)).value, weight = 1 });

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
			!string.IsNullOrEmpty(normalTextureFileName) && !string.IsNullOrEmpty(outOfStockFileName) && usesLeft > 0 && minAmount >= 0 && maxAmount > 0 && minAmount <= maxAmount && sodaMachineWeight > 0 && allowedLevels.Contains(floor);

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
			Plugin.lastlevelnum = 1;
			Plugin.builders.Clear();
		}
	}
}
