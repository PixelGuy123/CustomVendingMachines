using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using MTM101BaldAPI;
using MTM101BaldAPI.AssetTools;
using MTM101BaldAPI.Registers;
using Newtonsoft.Json;
using PixelInternalAPI;
using PixelInternalAPI.Extensions;
using UnityEngine;

namespace CustomVendingMachines
{
	[BepInDependency("mtm101.rulerp.bbplus.baldidevapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInDependency("pixelguy.pixelmodding.baldiplus.pixelinternalapi", BepInDependency.DependencyFlags.HardDependency)]
	[BepInPlugin("pixelguy.pixelmodding.baldiplus.customvendingmachines", PluginInfo.PLUGIN_NAME, "1.0.6")]
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

				Dictionary<VendingMachineData, WeightedGameObject> dataWeightPair = []; // To save memory

				if (!placer)
					placer = GenericExtensions.FindResourceObjectByName<Structure_EnvironmentObjectPlacer>("Structure_EnvironmentObjectBuilder_Weighted");

				List<VendingMachineData> machines = [];
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
						machines.Add(machine);


				}

				if (machines.Count == 0)
					return;

				WeightedGameObject[] GetProperArray(LevelObject levelObj)
				{
					var filteredMachines = machines.Where(x => x.convertedLevelTypes.Contains(levelObj.type));
					WeightedGameObject[] objArray = new WeightedGameObject[filteredMachines.Count()];
					int idx = 0;
					foreach (var machine in filteredMachines)
					{
						if (!dataWeightPair.TryGetValue(machine, out var weight))
						{
							weight = new() { selection = machine.machine.gameObject, weight = machine.sodaMachineWeight };
							dataWeightPair.Add(machine, weight);
						}
						objArray[idx++] = weight;
					}
					return objArray;
				}

				if (infiniteFloors)
				{
					foreach (var levelObject in sco.GetCustomLevelObjects())
					{
						levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()  // Presumably, any infinite floors port would be using the levelObject, not a randomized array
						{
							prefab = placer,
							parameters = new()
							{
								minMax = [new(Mathf.FloorToInt(2 * num * 0.35f), Mathf.FloorToInt(3.5f * num * 0.45f))],
								chance = [0.25f * num * 0.35f % 1f],
								prefab = GetProperArray(levelObject)
							}
						});
					}
					return;
				}
				foreach (var levelObject in sco.GetCustomLevelObjects())
				{
					switch (name)
					{
						default: break;

						case "F1":
							levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()
							{
								prefab = placer,
								parameters = new()
								{
									minMax = [new(2, 5)],
									chance = [0.5f],
									prefab = GetProperArray(levelObject)
								}
							});
							break;

						case "F2":
							levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()
							{
								prefab = placer,
								parameters = new()
								{
									minMax = [new(4, 6)],
									chance = [0.35f],
									prefab = GetProperArray(levelObject)
								}
							});
							break;

						case "F3":
							levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()
							{
								prefab = placer,
								parameters = new()
								{
									minMax = [new(6, 9)],
									chance = [0.65f],
									prefab = GetProperArray(levelObject)
								}
							});
							break;

						case "F4":
							levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()
							{
								prefab = placer,
								parameters = new()
								{
									minMax = [new(7, 10)],
									chance = [0.68f],
									prefab = GetProperArray(levelObject)
								}
							});
							break;

						case "F5":
							levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()
							{
								prefab = placer,
								parameters = new()
								{
									minMax = [new(8, 9)],
									chance = [0.72f],
									prefab = GetProperArray(levelObject)
								}
							});
							break;

						case "END":
							levelObject.forcedStructures = levelObject.forcedStructures.AddToArray(new()
							{
								prefab = placer,
								parameters = new()
								{
									minMax = [new(4, 7)],
									chance = [0.5f],
									prefab = GetProperArray(levelObject)
								}
							});
							break;
					}
				}
			});
		}

		void LoadVendingMachines()
		{
			if (initializedMachines) return;
			initializedMachines = true;

			static bool TryGetFromExtendedName<T>(string name, out T en) where T : Enum
			{
				try
				{
					en = EnumExtensions.GetFromExtendedName<T>(name);
					return true;
				}
				catch
				{
					en = default;
					return false;
				}
			}

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
						if (data.items[i].item == "None" || !TryGetFromExtendedName<Items>(data.items[i].item, out var itmEnum))
							throw new InvalidItemsEnumException(data.items[i].item);

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
					data.machine.name = "CustomVendingMachine_" + (data.items.Length == 1 ? data.items[0].item : "MultipleItems");
					data.machine.SetUses(data.usesLeft);
					data.machine.SetPotentialItems(weightedItems);

					// Acceptable items search
					if (data.acceptableItems.Length != 0)
					{
						HashSet<Items> acceptableItms = [];
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
								Debug.LogWarning($"CustomVendingMachines: Failed to find enum for acceptable item ({data.acceptableItems[i]}).");
							}
						}
						if (acceptableItms.Count != 0)
							data.machine.SetRequiredItems([.. acceptableItms]);
					}

					if (data.allowedLevelTypes.Length == 0)
					{
						var nums = EnumExtensions.GetValues<LevelType>();
						for (int i = 0; i < nums.Length; i++)
							data.convertedLevelTypes.Add((LevelType)nums[i]); // Adds all the available LevelType values (even modded ones)
					}
					else
					{
						for (int i = 0; i < data.allowedLevelTypes.Length; i++)
						{
							try // Works this way with strings to support customized LevelTypes
							{
								data.convertedLevelTypes.Add(EnumExtensions.GetFromExtendedName<LevelType>(data.allowedLevelTypes[i]));
							}
							catch
							{
								Debug.LogWarning($"CustomVendingMachines: failed to parse LevelType enum value: \"{data.allowedLevelTypes[i]}\"");
							}
						}
					}

					sodaMachines.Add(data);
				}
				catch (Exception e)
				{
					Debug.LogWarning("CustomVendingMachines: There was an error in the process of making vending machines!");
					Debug.LogError("Single-line error: " + e.Message);
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
		public string[] allowedLevelTypes = [];

		[JsonIgnore]
		public SodaMachine machine;
		[JsonIgnore]
		public HashSet<LevelType> convertedLevelTypes = [];
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
