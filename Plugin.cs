using BepInEx;
using HarmonyLib;
using MTM101BaldAPI.AssetTools;
using MTM101BaldAPI.Registers;
using PixelInternalAPI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;
using BepInEx.Bootstrap;
using MTM101BaldAPI;
using PixelInternalAPI.Extensions;
using System.Threading;

namespace CustomVendingMachines
{
	[BepInDependency("mtm101.rulerp.bbplus.baldidevapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("pixelguy.pixelmodding.baldiplus.pixelinternalapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInPlugin("pixelguy.pixelmodding.baldiplus.customvendingmachines", PluginInfo.PLUGIN_NAME, "1.0.5")]
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
					datas.Add(new(path, JsonConvert.DeserializeObject<VendingMachineData>(File.ReadAllText(file))));
				}
				catch (Exception e)
				{
					Debug.LogWarning("BBCustomVendingMachines: Failed to load json from path: " + Path.GetFileName(file));
					Debug.LogException(e);
					errors.Add("Failed to load json: " + Path.GetFileName(file));
				}
			}
		}
		private void Awake()
		{
			Harmony h = new("pixelguy.pixelmodding.baldiplus.customvendingmachines");
			h.PatchAll();

			//File.WriteAllText(Path.Combine(AssetLoader.GetModPath(this), "test.json"), JsonConvert.SerializeObject(new VendingMachineData()
			//{
			//	acceptableItems = ["someItem", "someitem2"],
			//	allowedLevels = ["F1", "F2", "F3", "END", "INF5"],
			//	items = [
			//		new() { item = "Quarter"},
			//		new() { item = "Present", weight = 50},
			//		],
			//}, Formatting.Indented));

			AddDataFromDirectory(AssetLoader.GetModPath(this)); // Read from the directory already

			Structure_EnvironmentObjectPlacer placer = null;

			GeneratorManagement.Register(this, GenerationModType.Addend, (name, num, sco) =>
			{
				LoadVendingMachines();

				if (!placer)
					 placer = GenericExtensions.FindResourceObjectByName<Structure_EnvironmentObjectPlacer>("Structure_EnvironmentObjectBuilder_Weighted");

				if (!sco.levelObject)
					return;

				List<WeightedGameObject> machines = [];
				bool infiniteFloors = name.StartsWith("INF");

				foreach (var machine in sodaMachines)
				{
					bool flag = false;
					if (infiniteFloors)
					{
						for (int i = 0; i < machine.allowedLevels.Length; i++)
						{
							int res = -1;
							if (machine.allowedLevels[i].StartsWith("INF") && 
							int.TryParse(machine.allowedLevels[i].Substring(3, machine.allowedLevels[i].Length - 3), out res) && // Gets the raw number after the "INF" prefix
							num >= res // Inf floor support
							)
							{
								flag = true;
								break;
							}
						}
					}
					else if (machine.allowedLevels.Contains(name))
						flag = true;
					
					if (flag)
						machines.Add(new() { selection = machine.machine.gameObject, weight = machine.sodaMachineWeight });
					

				}

				if (machines.Count == 0) 
					return;

				WeightedGameObject[] machinesArray = [.. machines];

				if (infiniteFloors) 
				{
					sco.levelObject.forcedStructures = sco.levelObject.forcedStructures.AddToArray(new() 
					{ prefab = placer, 
					parameters = new() 
						{ 
							minMax = [new(Mathf.FloorToInt(2 * num * 0.35f), Mathf.FloorToInt(3.5f * num * 0.45f))],
							chance = [0.25f * num * 0.35f % 1f],
							prefab = machinesArray
					} 
					});
					return;
				}

				switch (name)
				{
					default: break;

					case "F1":
						sco.levelObject.forcedStructures = sco.levelObject.forcedStructures = sco.levelObject.forcedStructures.AddToArray(new()
						{
							prefab = placer,
							parameters = new()
							{
								minMax = [new(2, 5)],
								chance = [0.5f],
								prefab = machinesArray
							}
						});
						break;

					case "F2":
						sco.levelObject.forcedStructures = sco.levelObject.forcedStructures = sco.levelObject.forcedStructures.AddToArray(new()
						{
							prefab = placer,
							parameters = new()
							{
								minMax = [new(4, 6)],
								chance = [0.35f],
								prefab = machinesArray
							}
						});
						break;

					case "F3":
						sco.levelObject.forcedStructures = sco.levelObject.forcedStructures = sco.levelObject.forcedStructures.AddToArray(new()
						{
							prefab = placer,
							parameters = new()
							{
								minMax = [new(6, 9)],
								chance = [0.65f],
								prefab = machinesArray
							}
						});
						break;

					case "END":
						sco.levelObject.forcedStructures = sco.levelObject.forcedStructures = sco.levelObject.forcedStructures.AddToArray(new()
						{
							prefab = placer,
							parameters = new()
							{
								minMax = [new(4, 7)],
								chance = [0.5f],
								prefab = machinesArray
							}
						});
						break;
				}
			});
		}

		void LoadVendingMachines()
		{
			if (initializedMachines) return;
			initializedMachines = true;

			static void ThrowIfInvalidTexture(Texture2D tex)
			{
				if (tex.width != 128 || tex.height != 224)
					throw new ArgumentOutOfRangeException($"Texture ({tex.name}) has an invalid height or width ({tex.width}x{tex.height})");
			}

			foreach (var dataPair in datas)
			{				
				var data = dataPair.Value;
				
				try
				{
					// Texture Load
					string texPath = Path.Combine(dataPair.Key, data.normalTextureFileName);
					if (!File.Exists(texPath))
						throw new FileNotFoundException($"Failed to find the main texture for the vending machine ({Path.GetFileName(texPath)})");

					Texture2D normalTexture = AssetLoader.TextureFromFile(texPath);
					ThrowIfInvalidTexture(normalTexture);

					Texture2D outOfStockTex = null;

					if (!string.IsNullOrEmpty(data.outOfStockTextureFileName)) 
					{
						texPath = Path.Combine(dataPair.Key, data.outOfStockTextureFileName);
						if (!File.Exists(texPath))
							throw new FileNotFoundException($"Failed to find the out of stock texture for the vending machine ({Path.GetFileName(texPath)})");

						outOfStockTex = AssetLoader.TextureFromFile(texPath);
						ThrowIfInvalidTexture(outOfStockTex);
					}

					// Item search
					WeightedItemObject[] weightedItems = new WeightedItemObject[data.items.Length];
					for (int i = 0; i < data.items.Length; i++)
					{
						if (data.items[i].item == "None" || string.IsNullOrEmpty(data.items[i].item))
							throw new InvalidItemsEnumException(data.items[i].item);

						var itmEnum = EnumExtensions.GetFromExtendedName<Items>(data.items[i].item);
						var itemMeta = (string.IsNullOrEmpty(data.items[i].mod_guid) ?
								ItemMetaStorage.Instance.FindByEnum(itmEnum) : ItemMetaStorage.Instance.FindByEnumFromMod(itmEnum, Chainloader.PluginInfos[data.items[i].mod_guid]))

								?? throw new InvalidItemsEnumException(data.items[i].item); // if itemMeta is null, throw this error

						weightedItems[i] = new WeightedItemObject()
						{
							weight = data.items[i].weight,
							selection = itemMeta.value
						};
					}

					data.machine = ObjectCreationExtensions.CreateSodaMachineInstance(normalTexture, outOfStockTex ?? normalTexture);
					data.machine.AddNewPotentialItems(weightedItems);

					// Acceptable items search
					if (data.acceptableItems.Length != 0)
					{
						List<Items> acceptableItms = [];
						for (int i = 0; i < data.acceptableItems.Length; i++)
						{
							if (string.IsNullOrEmpty(data.acceptableItems[i]))
								throw new InvalidItemsEnumException(data.acceptableItems[i]);

							try
							{
								acceptableItms.Add(EnumExtensions.GetFromExtendedName<Items>(data.acceptableItems[i]));
							}
							catch
							{
								Debug.LogWarning($"Failed to find enum for acceptable item ({data.acceptableItems[i]}).");
							}
						}
						if (acceptableItms.Count != 0)
							data.machine.AddNewRequiredItems([.. acceptableItms]);
					}

					sodaMachines.Add(data);
				}
				catch(Exception e)
				{
					Debug.LogException(e);
					errors.Add(e.Message);
				}
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
		}

		static bool initializedMachines = false;

		readonly static List<string> errors = [];

		internal static int lastlevelnum = 1;

		readonly internal List<VendingMachineData> sodaMachines = [];

		readonly static List<KeyValuePair<string, VendingMachineData>> datas = [];
	}

	class InvalidItemsEnumException(string invalidEnumName) : Exception($"An Item that doesn\'t exist in the meta storage ({invalidEnumName})");

	[JsonObject]
	class VendingMachineData
	{
		public VendingMachineData() =>
			usesLeft = Mathf.Max(usesLeft, 1);

		[JsonRequired]
		public int sodaMachineWeight = 100;

		public int usesLeft = 1;
		[JsonRequired]
		public string normalTextureFileName = string.Empty;

		public string outOfStockTextureFileName = string.Empty;

		[JsonRequired]
		public VendingMachineItem[] items = [];

		public string[] acceptableItems = [];

		[JsonRequired]
		public string[] allowedLevels = [];

		[JsonIgnore]
		public SodaMachine machine;
	}

	[JsonObject]
	class VendingMachineItem
	{
		[JsonRequired]
		public string item = "None";
		public int weight = 100;
		public string mod_guid = string.Empty;
	}

	// When you quit the game, it should always lead to this screen anyways
	[HarmonyPatch(typeof(MainMenu), "Start")]
	class ResetIncludedBuildersPatch
	{
		private static void Prefix() =>
			CustomVendingMachinesPlugin.lastlevelnum = 1;
	}
}
