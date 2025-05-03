using GuildsAndGlory.BuildingSystems;
using GuildsAndGlory.Economy;
using GuildsAndGlory.MainSystems;
using GuildsAndGlory.SerfsSystems;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace GuildsAndGlory.Buildings
{
    /// <summary>
    /// The BuildingWareFactory class handles the production process within a building,
    /// including recipe selection, resource verification, and delivery requests.
    /// It supports both production buildings (e.g. smithies) and resource-gathering structures (e.g. woodcutters).
    /// The system automatically checks if required materials are available and requests them if necessary.
    /// Production proceeds through a coroutine that simulates time-based crafting or gathering.
    /// </summary>
    public class BuildingWareFactory : MonoBehaviour
    {
        [SerializeField] private BuildingBase closestPlayerStorage;

        // Events
        public event Action<ProductionRecipeSO> OnProductionFinished;

        private List<ProductionRecipeSO> availableRecipes;
        private Dictionary<ProductionRecipeSO, int> activeRecipes = new Dictionary<ProductionRecipeSO, int>();

        private ProductionBuildingStorage inputBuildingStorage;
        private ProductionBuildingStorage outputBuildingStorage;

        private BuildingBase buildingBase;
        private BuildingTypeSO buildingType;

        private ProductionRecipeSO selectedRecipe;

        private float timer;
        private int progressPercent;
        private bool isProducing = false;

        private Dictionary<WareTypeSO, float> lastRequestTime = new Dictionary<WareTypeSO, float>();
        private const float REQUEST_COOLDOWN = 10f;

        private int standingGathersAmount = 0;

        /// <summary>
        /// Initializes this production component with recipe and storage references.
        /// </summary>
        public void InitializeProductionSettings(BuildingTypeSO buildingType, BuildingBase buildingBase)
        {
            this.buildingBase = buildingBase;
            availableRecipes = new List<ProductionRecipeSO>(buildingType.productionRecipes);

            if (buildingType.isProductionBuilding)
            {
                inputBuildingStorage = (ProductionBuildingStorage)buildingBase.GetBuildingInputStorage();
            }
            outputBuildingStorage = (ProductionBuildingStorage)buildingBase.GetBuildingOutputStorage();

            this.buildingType = buildingType;
            SetSelectedRecipe(buildingType.productionRecipes[0]);
            closestPlayerStorage = PlayerBuildingManager.Instance.FindNearestWarehouse(transform.position);
        }

        /// <summary>
        /// Attempts to start the production process if requirements are met.
        /// </summary>
        public bool TryProduceWare()
        {
            if (isProducing) return false;

            if (buildingType.isProductionBuilding)
            {
                int maxOutputWareCapacity = outputBuildingStorage.GetMaxAmountPerWare();
                int outputWareAmount = outputBuildingStorage.GetWareAmount(selectedRecipe.outputWare);

                if (outputWareAmount + selectedRecipe.outputWareAmount > maxOutputWareCapacity)
                {
                    Debug.Log($"TryProduceWare: Not enough space in output storage! Have {outputWareAmount}/{maxOutputWareCapacity}.");
                    return false;
                }

                if (HasNeededWares())
                {
                    isProducing = true;
                    StartCoroutine(ProcessProduction());
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else if(buildingType.isResourceGatherBuilding)
            {
                isProducing = true;
                StartCoroutine(ProcessGathering());
                return true;
            }

            return false;
        }

        #region Producinig Methods

        /// <summary>
        /// Processes a time-based production cycle for standard production buildings.
        /// Consumes ingredients and outputs the crafted ware.
        /// </summary>
        private IEnumerator ProcessProduction()
        {
            foreach(RecipeIngredient ingredient in selectedRecipe.ingredients)
            {
                inputBuildingStorage.RemoveWare(ingredient.wareType, ingredient.amount);
            }

            timer = 0f;
            while(timer < selectedRecipe.productionTime)
            {
                timer += Time.deltaTime;
                progressPercent = Mathf.FloorToInt((timer / selectedRecipe.productionTime) * 100);
                yield return null;
            }

            progressPercent = 100;
            outputBuildingStorage.AddWare(selectedRecipe.outputWare, selectedRecipe.outputWareAmount);
            int newAmount = outputBuildingStorage.GetWareAmount(selectedRecipe.outputWare);

            isProducing = false;

            if (activeRecipes.ContainsKey(selectedRecipe))
            {
                int currentOrders = activeRecipes[selectedRecipe];
                activeRecipes[selectedRecipe] = Mathf.Max(0, currentOrders - 1);
            }
            OnProductionFinished?.Invoke(selectedRecipe);
        }

        /// <summary>
        /// Processes a time-based cycle for gathering buildings (e.g. fishing huts).
        /// </summary>
        private IEnumerator ProcessGathering()
        {
            timer = 0f;
            while (timer < selectedRecipe.productionTime)
            {
                timer += Time.deltaTime;
                progressPercent = Mathf.FloorToInt((timer / selectedRecipe.productionTime) * 100);
                yield return null;
            }
            progressPercent = 100;
            outputBuildingStorage.AddWare(selectedRecipe.outputWare, selectedRecipe.outputWareAmount);
            isProducing = false;

            if (activeRecipes.ContainsKey(selectedRecipe))
            {
                int currentOrders = activeRecipes[selectedRecipe];
                activeRecipes[selectedRecipe] = Mathf.Max(0, currentOrders - 1);
            }
            OnProductionFinished?.Invoke(selectedRecipe);
        }

        #endregion

        #region Production Request Methods

        /// <summary>
        /// Enables or disables a recipe in the active order queue.
        /// </summary>
        public void SetRecipeActive(ProductionRecipeSO recipe, bool active)
        {
            if (active)
            {
                if (!activeRecipes.ContainsKey(recipe))
                    activeRecipes[recipe] = 0;
            }
            else
            {
                if (activeRecipes.ContainsKey(recipe))
                    activeRecipes.Remove(recipe);
            }
        }
        public bool IsRecipeActive(ProductionRecipeSO recipe)
        {
            return activeRecipes.ContainsKey(recipe);
        }
        public void SetProductionOrderAmount(ProductionRecipeSO recipe, int amount)
        {
            if(activeRecipes.ContainsKey(recipe))
                activeRecipes[recipe] = amount;
            else
                activeRecipes[recipe] = amount;
        }
        public int GetProductionOrderAmount(ProductionRecipeSO recipe)
        {
            return activeRecipes.TryGetValue(recipe, out int amount) ? amount : 0;
        }


        #endregion

        #region Delivery Request Calculations

        /// <summary>
        /// Checks if all required ingredients are present in the input storage.
        /// If not, triggers material request tasks.
        /// </summary>
        private bool HasNeededWares()
        {
            foreach (RecipeIngredient ingredient in selectedRecipe.ingredients)
            {
                if (!inputBuildingStorage.HasWare(ingredient.wareType, ingredient.amount))
                {
                    List<WareEntry> missingWares = ComputeMissingWares();
                    RequestMissingMaterials(missingWares);
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Requests the delivery of missing materials from the nearest warehouse.
        /// </summary>
        private void RequestMissingMaterials(List<WareEntry> missing)
        {
            foreach (var material in missing)
            {
                if (ShouldRequestWare(material.wareType))
                {
                    RequestWares(buildingBase, new List<WareEntry> { material });
                    lastRequestTime[material.wareType] = Time.time;
                }
            }
        }

        /// <summary>
        /// Prevents frequent repeated requests for the same material.
        /// </summary>
        private bool ShouldRequestWare(WareTypeSO wareType)
        {
            if (!lastRequestTime.ContainsKey(wareType)) return true;
            return Time.time - lastRequestTime[wareType] >= REQUEST_COOLDOWN;
        }

        /// <summary>
        /// Sends a request to the CarrierManagerSystem to deliver materials.
        /// </summary>
        public void RequestWares(BuildingBase building, List<WareEntry> requiredMaterials)
        {
            foreach (var material in requiredMaterials)
            {
                // Check if task already exists
                if (CarrierManagerSystem.Instance.HasActiveTaskFor(building, material.wareType))
                {
                    Debug.Log($"Task for {material.wareType.name} already exists");
                    continue;
                }

                // Create single task for total amount
                var task = CarrierManagerSystem.Instance.CreateDeliveryRequest(
                    closestPlayerStorage,
                    StorageType.Output,
                    building,
                    StorageType.Input,
                    material.wareType,
                    material.wareAmount
                );

                if (task == null)
                {
                    Debug.LogWarning($"Failed to create task for {material.wareType.name}");
                }
            }
        }

        /// <summary>
        /// Calculates how many units of each ware are missing.
        /// </summary>
        private List<WareEntry> ComputeMissingWares()
        {
            List<WareEntry> missingWares = new List<WareEntry>();
            Dictionary<WareTypeSO, int> storageWares = new Dictionary<WareTypeSO, int>(inputBuildingStorage.GetAllWares());

            foreach (var required in selectedRecipe.ingredients)
            {
                int current = 0;
                if (inputBuildingStorage.GetAllWares().ContainsKey(required.wareType))
                    current = storageWares[required.wareType];

                int diff = required.amount - current;
                if (diff > 0)
                {
                    missingWares.Add(new WareEntry
                    {
                        wareType = required.wareType,
                        wareAmount = diff
                    });
                }
            }

            return missingWares;
        }

        #endregion

        public void SetSelectedRecipe(ProductionRecipeSO recipe)
        {
            if (availableRecipes.Contains(recipe))
                selectedRecipe = recipe;
            else
                Debug.LogError($"Recipe {recipe.name} is not available in {buildingType.name}");
        }
        public bool IsProducing() { return isProducing; }
        public bool HasActiveRecipes()
        {
            foreach (var kvp in activeRecipes)
            {
                if (kvp.Value > 0)
                    return true;
            }
            return false;
        }

        public Dictionary<ProductionRecipeSO, int> GetActiveRecipes()
        {
            return activeRecipes;
        }
        public ProductionRecipeSO GetNextActiveRecipe()
        {
            foreach (var kvp in activeRecipes)
            {
                if (kvp.Value > 0)
                    return kvp.Key;
            }
            return null;
        }
        public int GetStandingGathersAmount() => standingGathersAmount;
        public void IncreaseStandingGathersAmount() => standingGathersAmount++;
    }
}
