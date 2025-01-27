using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using WarlordsOfArcania.GameSystems;
using WarlordsOfArcania.Units;
using WarlordsOfArcania.Utilities;

namespace WarlordsOfArcania.UI
{
    public class UnitPlacementUI : MonoBehaviour
    {
        /// <summary>
        /// Manages the UI for unit placement before the battle. Handles unit selection, grid validation, 
        /// and placement, restricting units to valid positions. Once units are placed, it hides the UI 
        /// and starts the battle.
        /// </summary>
        public static UnitPlacementUI Instance {  get; private set; }

        [SerializeField] private Button battleStartButton;
        [SerializeField] private Transform unitListContainer;
        [SerializeField] private GameObject unitButtonAvatarPrefab;

        private List<Unit> placedUnits = new List<Unit>();
        private AdventureMapUnitState selectedUnitState;
        private GameObject unitPreviewInstance;
        private Dictionary<AdventureMapUnitState, UnitSelectionButtonUI> unitButtonDictionary = new Dictionary<AdventureMapUnitState, UnitSelectionButtonUI>();

        // New variable to track selected placed unit if changing position of allready placed one
        private Unit selectedPlacedUnit;

        private UnitSelectionButtonUI selectedButton;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("Multiple instances of UnitPlacementUI detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Assigns the StartBattle method to the button's click event
            battleStartButton.onClick.AddListener(StartBattle);
        }

        /// <summary>
        /// Updates the position of the unit prefab to follow the mouse cursor.
        /// Validates the grid position and handles unit placement upon left mouse click.
        /// </summary>
        private void Update()
        {
            // Check if it is correct battle state
            if (!(BattleManager.Instance.GetCurrentBattleState() == BattleManager.BattleState.UnitPlacement)) { return; }

            // Updates the unit preview to follow the mouse
            if (unitPreviewInstance != null)
            {
                Vector3 mousePosition = MouseWorld.GetWorldMousePosition();
                GridPosition gridPosition = LevelGrid.Instance.GetGridPosition(mousePosition);

                // Restrict unit placement to the first three columns of the grid
                if (LevelGrid.Instance.IsValidGridPosition(gridPosition) && gridPosition.x < 3)
                {
                    unitPreviewInstance.transform.position = LevelGrid.Instance.GetWorldPosition(gridPosition);
                }

                // Place the unit on the grid when left mouse button is clicked
                if (InputManager.Instance.IsLMBDown())
                {
                    TryPlaceUnitOnGrid(gridPosition);
                }

                // Cancel unit placement and destroy the unit preview when right mouse button is clicked
                if (InputManager.Instance.IsRMBDown())
                {
                    CancelUnitPlacement();
                }
            }
            else
            {
                // If no unit is currently selected, allow selecting an already placed unit
                if (InputManager.Instance.IsLMBDown())
                {
                    TrySelectPlacedUnit();
                }
            }
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Confirms the unit placement and starts the battle. Hides the UI elements after the battle begins.
        /// </summary>
        private void StartBattle()
        {
            if (placedUnits.Count > 0) // Ensures at least one unit is placed before starting
            {
                BattleManager.Instance.ConfirmUnitPlacement(placedUnits);
                battleStartButton.gameObject.SetActive(false);
                unitListContainer.gameObject.SetActive(false);
            }
            else
            {
                MessageWindowUI.Instance.ShowMessage("At least one unit must be placed", .3f);
            }
        }

        /// <summary>
        /// Displays available units for placement and creates buttons for each unit.
        /// </summary>
        /// <param name="playerUnits">List of units from the player's army.</param>
        public void ShowUnitsForPlacements(List<AdventureMapUnitState> playerUnits)
        {
            foreach (var unitState in playerUnits)
            {
                GameObject unitAvatarButton = Instantiate(unitButtonAvatarPrefab, unitListContainer);
                UnitSelectionButtonUI avatarButtonUI = unitAvatarButton.GetComponent<UnitSelectionButtonUI>();

                // Set the unit and subscribe to the OnUnitSelected event
                avatarButtonUI.SetUnit(unitState);
                avatarButtonUI.OnUnitSelected += HandleUnitSelected;

                // Store reference to the unit button for later removal
                unitButtonDictionary[unitState] = avatarButtonUI;
            }
        }

        /// <summary>
        /// Handles the selection of a unit from the UI. Updates the selected unit and creates a preview instance.
        /// </summary>
        /// <param name="selectedUnit">The unit selected by the player.</param>
        private void HandleUnitSelected(AdventureMapUnitState selectedUnit)
        {
            selectedUnitState = selectedUnit;

            if(selectedButton != null)
            {
                selectedButton.ChangeFrame(false);
            }

            selectedButton = unitButtonDictionary[selectedUnit];
            selectedButton.ChangeFrame(true);

            // Destroy any existing unit preview instance
            if (unitPreviewInstance != null)
            {
                Destroy(unitPreviewInstance);
            }

            // Instantiate a new unit prefab to follow the mouse
            unitPreviewInstance = Instantiate(selectedUnit.unitType.unitPrefab);
        }

        /// <summary>
        /// Attempts to place the selected unit on the specified grid position.
        /// Validates the position to ensure it's within the allowed range and is unoccupied.
        /// </summary>
        /// <param name="gridPosition">The grid position where the unit is to be placed.</param>
        private void TryPlaceUnitOnGrid(GridPosition gridPosition)
        {
            // Validate that the unit is being placed in the first three columns
            if (gridPosition.x >= 3)
            {
                MessageWindowUI.Instance.ShowMessage("Invalid position. You can only place units in the first three columns.", 2f);
                return;
            }

            // Check if the grid position is valid, unoccupied, and walkable
            if (!LevelGrid.Instance.IsValidGridPosition(gridPosition) ||
                LevelGrid.Instance.HasAnyUnitAtGridPosition(gridPosition) ||
                !Pathfinding.Instance.IsWalkableGridPosition(gridPosition))
            {
                MessageWindowUI.Instance.ShowMessage("Invalid position for unit placement.", 1f);
                return;
            }

            if (selectedPlacedUnit != null)
            {
                // Relocate the already placed unit
                LevelGrid.Instance.AddUnitGridPosition(gridPosition, selectedPlacedUnit);
                selectedPlacedUnit.transform.position = LevelGrid.Instance.GetWorldPosition(gridPosition);
                selectedPlacedUnit.PlaceUnitOnGrid();
                GridSystemVisual.Instance.UpdatePlayerUnitPlacementZoneWithValidation();

                selectedPlacedUnit.GetUnitSoundController().PlaySound(SoundManager.Sound.UnitPlaced);

                // Clear the preview and reset the selected placed unit
                unitPreviewInstance = null;
                selectedPlacedUnit = null;

            }
            else
            {
                Unit newUnit = unitPreviewInstance.GetComponent<Unit>();

                // Place the unit and add it to the list of placed units
                placedUnits.Add(newUnit);
                newUnit.InitializeUnit(selectedUnitState);
                LevelGrid.Instance.AddUnitGridPosition(gridPosition, newUnit);

                // Update the grid visuals and remove the unit's button from the UI
                newUnit.PlaceUnitOnGrid();
                GridSystemVisual.Instance.UpdatePlayerUnitPlacementZoneWithValidation();
                RemoveUnitButton(selectedUnitState);

                newUnit.SubscribeOnDeadEvent();
                newUnit.GetUnitSoundController().PlaySound(SoundManager.Sound.UnitPlaced);

                // Clear the preview instance
                unitPreviewInstance = null;

                // Enable the start battle button as at least one unit has been placed
                // battleStartButton.interactable = true;
            }
        }

        /// <summary>
        /// Removes the UI button corresponding to the placed unit.
        /// </summary>
        /// <param name="unitState">The state of the unit that was placed.</param>
        private void RemoveUnitButton(AdventureMapUnitState unitState)
        {
            if (unitButtonDictionary.TryGetValue(unitState, out UnitSelectionButtonUI buttonUI))
            {
                Destroy(buttonUI.gameObject);
                unitButtonDictionary.Remove(unitState);
            }
        }

        /// <summary>
        /// Cancels the current unit placement by destroying the unit preview.
        /// </summary>
        private void CancelUnitPlacement()
        {
            if (unitPreviewInstance != null && selectedPlacedUnit == null)
            {
                CancelNewUnitPlacement();

                if (selectedButton != null)
                {
                    selectedButton.ChangeFrame(false);
                    selectedButton = null;
                }
            }
            else if (selectedPlacedUnit != null)
            {
                CancelPlacedUnitRelocation();

                if (selectedButton != null)
                {
                    selectedButton.ChangeFrame(false);
                    selectedButton = null;
                }
            }
        }

        private void CancelNewUnitPlacement()
        {
            Destroy(unitPreviewInstance);
            unitPreviewInstance = null;
            GridSystemVisual.Instance.UpdatePlayerUnitPlacementZoneWithValidation();
        }
        private void CancelPlacedUnitRelocation()
        {
            Debug.Log($"Unit {selectedPlacedUnit.name} returned to the pool of available units.");

            selectedPlacedUnit.UnsubscribeOnDeadEvent();

            // Add the unit back to the selection pool
            AddUnitBackToPool(selectedPlacedUnit);

            // Remove the unit from placed units list
            placedUnits.Remove(selectedPlacedUnit);

            // Destroy or remove the unit instance
            Destroy(selectedPlacedUnit.gameObject);
            selectedPlacedUnit = null;
            unitPreviewInstance = null;
            GridSystemVisual.Instance.UpdatePlayerUnitPlacementZoneWithValidation();
        }

        /// <summary>
        /// Adds the canceled unit back to the pool of available units by recreating its selection button.
        /// </summary>
        private void AddUnitBackToPool(Unit unit)
        {
            List<AdventureMapUnitState> playerUnits = PlayerHero.Instance.AdventureMapPlayerArmy.GetPlayerUnits();

            if (unit.GetUnitName() == "Hero")
            {
                AdventureMapUnitState playerHeroUnit = PlayerHero.Instance.PlayerHeroState.PlayerHeroUnitState;
                playerUnits.Add(playerHeroUnit);
            }
            AdventureMapUnitState matchingUnitState = playerUnits.Find(unitState => unitState.unitID == unit.GetUnitID());

            if (matchingUnitState != null)
            {
                // Create the unit's selection button again
                GameObject unitAvatarButton = Instantiate(unitButtonAvatarPrefab, unitListContainer);
                UnitSelectionButtonUI avatarButtonUI = unitAvatarButton.GetComponent<UnitSelectionButtonUI>();

                // Set the unit in the button and assign the selection event
                avatarButtonUI.SetUnit(matchingUnitState);
                avatarButtonUI.OnUnitSelected += HandleUnitSelected;

                // Add the button to the dictionary for tracking
                unitButtonDictionary[matchingUnitState] = avatarButtonUI;

                Debug.Log($"Unit {matchingUnitState.unitType.unitName} added back to the selection pool.");
            }
            else
            {
                Debug.LogError($"Unit with ID {unit.GetUnitID()} not found in player's army.");
            }
        }

        /// <summary>
        /// Allows the player to select an already placed unit and reattach it to the mouse for repositioning.
        /// </summary>
        private void TrySelectPlacedUnit()
        {
            Vector3 mousePosition = MouseWorld.GetWorldMousePosition();
            GridPosition gridPosition = LevelGrid.Instance.GetGridPosition(mousePosition);

            // Check if gridPosition is valid and within levelGrid
            if (!LevelGrid.Instance.IsValidGridPosition(gridPosition))
            {
                Debug.Log("Invalid grid position. Clicked outside the level grid.");
                return; // Abandon if clicked outside the grid area
            }

            // Check if there is a unit at the current grid postion
            if (LevelGrid.Instance.HasAnyUnitAtGridPosition(gridPosition))
            {
                Unit unit = LevelGrid.Instance.GetUnitAtGridPosition(gridPosition);

                if(unit != null && placedUnits.Contains(unit))
                {
                    Debug.Log($"Unit selected for repositioning: {unit.name}");

                    // Remove the unit from the grid and set it to be preview instance
                    LevelGrid.Instance.RemoveUnitAtGridPosition(gridPosition, unit);
                    GridSystemVisual.Instance.UpdatePlayerUnitPlacementZoneWithValidation();
                    unitPreviewInstance = unit.gameObject;
                    selectedPlacedUnit = unit; // Track the selected unit for repositioning

                    // Set the unit to "moving" state, so it doesn't update its grid position
                    unit.ResetPlacement();
                }
            }
        }
    }
}

