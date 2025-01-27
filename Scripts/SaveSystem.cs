using System;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using WarlordsOfArcania.Quests;
using System.Collections.Generic;
using WarlordsOfArcania.Inventory;
using WarlordsOfArcania.Armies;
using System.Linq;

namespace WarlordsOfArcania.GameSystems
{
    public static class SaveSystem
    {
        private static string lastSaveFileName;

        private static bool isLoadGameRequested = false;
        public static bool IsLoadGameRequested => isLoadGameRequested;

        public static void PrepareLoadGame(string saveFileName)
        {
            lastSaveFileName = saveFileName;
            isLoadGameRequested = true;
        }
        public static void ClearLoadGameRequest()
        {
            lastSaveFileName = null;
            isLoadGameRequested = false;
        }

        /// <summary>
        /// Saves the game data to a JSON file.
        /// </summary>
        public static void SaveGame(QuestManager questManager, LocationManager locationManager, string saveFileName)
        {
            string savePath = Application.persistentDataPath + "/" + saveFileName + ".json";

            SaveData saveData = new SaveData
            {
                saveName = saveFileName,
                worldMapName = SceneLoader.Instance.GetCurrentAdventureMap().mapName,
                saveDate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),

                // Locations Data
                locationUnitsMap = locationManager.GetLocationsUnitsState(),
                waresAtLocations = ConvertWaresAtLocationsToSerializable(locationManager.GetWaresAtLocations()),
                silverAtLocations = locationManager.GetSilverAtLocations(),
                questsAtLocations = locationManager.GetQuestsAtLocations(),
                itemsToCraftAtLocations = locationManager.GetItemsToCraftAtLocations(),

                // Player Data
                playerHeroState = new SerializablePlayerHeroState(PlayerHero.Instance.PlayerHeroState),
                activeQuests = questManager.GetActiveQuestsForSave(),
                playerUnits = PlayerHero.Instance.AdventureMapPlayerArmy.GetPlayerUnits(),
                playerInventory = ConvertPlayerInventoryToSerializable(PlayerHero.Instance.Inventory.GetInventory()),
                playerEquipment = new SerializableEquipment(PlayerHero.Instance.Equipment)
            };

            string json = JsonConvert.SerializeObject(saveData, Formatting.Indented);

            File.WriteAllText(savePath, json);

            Debug.Log("Game Saved to " + savePath);
        }


        /// <summary>
        /// Load the game data from a JSON file.
        /// </summary>
        public static void LoadGameDataAfterSceneLoad()
        {
            if (!isLoadGameRequested || string.IsNullOrEmpty(lastSaveFileName))
            {
                Debug.LogWarning("Load game was not requested or save file name is empty.");
                return;
            }
            if (LocationManager.Instance == null)
            {
                Debug.LogError("LocationManager not found on scene.");
                return;
            }
            if(QuestManager.Instance == null)
            {
                Debug.LogError("QuestManager not found on scene.");
                return;
            }
            if (PlayerHero.Instance == null)
            {
                Debug.LogError("PlayerHero not found on scene.");
                return;
            }

            string savePath = Application.persistentDataPath + "/" + lastSaveFileName + ".json";

            if (File.Exists(savePath))
            {
                string json = File.ReadAllText(savePath);
                SaveData saveData = JsonConvert.DeserializeObject<SaveData>(json);

                LocationManager locationManager = LocationManager.Instance;
                QuestManager questManager = QuestManager.Instance;
                AdventureMapPlayerArmy playerArmy = PlayerHero.Instance.AdventureMapPlayerArmy;
                PlayerHeroInventory playerInventory = PlayerHero.Instance.Inventory;
                PlayerHeroState playerHeroState = PlayerHero.Instance.PlayerHeroState;

                if (locationManager != null && 
                    questManager != null && 
                    playerArmy != null && 
                    playerInventory != null &&
                    playerHeroState != null)
                {
                    Debug.Log("Load process started.");

                    // Restore Locations data
                    locationManager.SetLocationUnitsState(saveData.locationUnitsMap);
                    foreach (var unitList in saveData.locationUnitsMap.Values)
                    {
                        foreach (var unitState in unitList)
                        {
                            unitState.RestoreUnitType();
                            //unitState.InitializeFromSerializedData();
                        }
                    }

                    Dictionary<string, InventoryContainer> restoredWaresAtLocations = new Dictionary<string, InventoryContainer>();
                    foreach (var kvp in saveData.waresAtLocations)
                    {
                        restoredWaresAtLocations[kvp.Key] = kvp.Value.ToInventoryContainer();
                    }
                    locationManager.SetWaresAtLocations(restoredWaresAtLocations);

                    locationManager.SetSilverAtLocations(saveData.silverAtLocations);
                    locationManager.SetQuestsAtLocations(saveData.questsAtLocations);
                    locationManager.SetItemsToCraftAtLocations(saveData.itemsToCraftAtLocations);

                    // Restore Player Hero State
                    playerHeroState.SetPlayerHeroState(saveData.playerHeroState);

                    // Restore Active Quests
                    questManager.LoadActiveQuestsFromSave(saveData.activeQuests);

                    // Restore Player Units
                    foreach (var unitState in saveData.playerUnits)
                    {
                        unitState.RestoreUnitType();
                    }
                    playerArmy.SetPlayerUnits(saveData.playerUnits);

                    // Restore Player Inventory
                    playerInventory.SetPlayerInventory(saveData.playerInventory.ToInventoryContainer());

                    // Restore Player Equipment
                    if (saveData.playerEquipment != null)
                    {
                        saveData.playerEquipment.ApplyToPlayerEquipment(PlayerHero.Instance.Equipment);
                    }

                    Debug.Log("Load process completed.");
                }
                else
                {
                    Debug.LogError("One or more managers not found on scene.");
                }

                Debug.Log("Game Loaded from " + savePath);
                isLoadGameRequested = false;
            }
            else
            {
                Debug.LogWarning("Save file not found in " + savePath);
            }
        }

        public static string GetLastSaveFileName()
        {
            string savePath = Application.persistentDataPath;
            if(!Directory.Exists(savePath))
            {
                Debug.LogWarning("Save directory not found.");
                return null;
            }

            var saveFiles = Directory.GetFiles(savePath, "*.json").Select(f => new FileInfo(f)).OrderByDescending(f => f.LastWriteTime).ToList();

            return saveFiles.Count > 0 ? saveFiles[0].FullName : null;
        }

        private static Dictionary<string, SerializableInventoryContainer> ConvertWaresAtLocationsToSerializable
            (Dictionary<string, InventoryContainer> waresAtLocations)
        {
            var serializableWaresAtLocations = new Dictionary<string, SerializableInventoryContainer>();
            foreach (var kvp in waresAtLocations)
                serializableWaresAtLocations[kvp.Key] = new SerializableInventoryContainer(kvp.Value);

            return serializableWaresAtLocations;
        }
        private static SerializableInventoryContainer ConvertPlayerInventoryToSerializable(InventoryContainer inventory)
        {
            SerializableInventoryContainer playerSerializableInventory = new SerializableInventoryContainer(inventory);
            return playerSerializableInventory;
        }
    }
}

