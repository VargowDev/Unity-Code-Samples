using System;
using System.Collections.Generic;
using UnityEngine;
using WarlordsOfArcania.Armies;
using WarlordsOfArcania.Units;
using WarlordsOfArcania.UI;

namespace WarlordsOfArcania.GameSystems
{
    /// <summary>
    /// The TurnSystem class manages the turn-based system in the game, keeping track of the units
    /// involved in combat, controlling the turn queue, and determining which unit acts next.
    /// It also manages the player's turn and ensures that dead units are properly removed from the turn queue.
    /// </summary>
    public class TurnSystem : MonoBehaviour
    {
        public static TurnSystem Instance { get; private set; }

        public event EventHandler OnTurnChanged;

        private List<Unit> unitsInBattle = new List<Unit>();
        private List<Unit> turnQueue;
        private int currentUnitIndex = 0;
        private Unit activeUnitInQueue;
        private bool isInitialized = false;
        private bool isPlayerTurn;
        AdventureMapPlayerArmy adventureMapPlayerArmy;

        private void Awake()
        {
            if(Instance != null)
            {
                Debug.Log("There's more than one TurnSystem! " + transform + " - " + Instance);
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }
        private void Start()
        {
            adventureMapPlayerArmy = PlayerHero.Instance.AdventureMapPlayerArmy;
        }
        private void Update()
        {
            if (isInitialized && UnitActionSystem.Instance != null)
            {
                // Subscribe to the turn end event once the system is initialized
                UnitActionSystem.Instance.OnUnitTurnEnd += UnitActionSystem_OnUnitTurnEnd;
                isInitialized = false; // Ensure it runs only once
            }
        }
        private void OnDisable()
        {
            if (UnitActionSystem.Instance != null)
            {
                UnitActionSystem.Instance.OnUnitTurnEnd -= UnitActionSystem_OnUnitTurnEnd;
            }

            // Unsubscribe from all unit OnDead events
            foreach (Unit unit in unitsInBattle)
            {
                unit.GetUnitStats().OnDead -= Unit_OnDead;
            }
        }

        /// <summary>
        /// First TurnSystem initialization in the beginning of the battle
        /// </summary>
        public void InitializeTurnSystem(List<Unit> playerUnits, List<Unit> enemyUnits)
        {
            // Clear previous units and add new ones
            unitsInBattle.Clear();
            unitsInBattle.AddRange(playerUnits);
            unitsInBattle.AddRange(enemyUnits);

            // Initialize turn queue
            InitializeTurnQueue();

            // Subscribe to OnDead event for all units
            foreach (Unit unit in unitsInBattle)
            {
                unit.GetUnitStats().OnDead += Unit_OnDead;
            }

            activeUnitInQueue = turnQueue[0];
            isInitialized = true;

            // Set initial player turn based on the first active unit
            isPlayerTurn = !activeUnitInQueue.IsEnemy();
        }

        /// <summary>
        /// Handles the event when the current unit's turn ends, and advances to the next unit in the queue.
        /// </summary>
        private void UnitActionSystem_OnUnitTurnEnd(object sender, System.EventArgs e)
        {
            NextTurn();
        }

        /// <summary>
        /// Called when a unit dies. Removes the unit from both its army and the turn queue,
        /// then ensures the turn queue continues without the dead unit.
        /// </summary>
        private void Unit_OnDead(object sender, System.EventArgs e)
        {
            UnitStats deadUnitStats = sender as UnitStats;
            Unit deadUnit = deadUnitStats.GetUnit();

            if (deadUnit != null)
            {
                // Removeing units from armies
                if(deadUnit.IsEnemy())
                {
                    EnemyArmy.Instance.AddUnitToUnitList(deadUnit, EnemyArmy.Instance.GetDeadUnitListReference());
                    EnemyArmy.Instance.RemoveUnitFromUnitList(deadUnit, EnemyArmy.Instance.GetUnitListReference());
                }
                else
                {
                    PlayerArmy.Instance.AddUnitToUnitList(deadUnit, PlayerArmy.Instance.GetDeadUnitList());
                    PlayerArmy.Instance.RemoveUnitFromUnitList(deadUnit, PlayerArmy.Instance.GetUnitList());
                    adventureMapPlayerArmy.RemoveUnitFromArmy(deadUnit);
                }

                unitsInBattle.Remove(deadUnit);
                int deadUnitIndex = turnQueue.IndexOf(deadUnit);
                turnQueue.Remove(deadUnit);

                // Update the UI
                TurnQueueUI.Instance.RemoveAllUnitAvatars(deadUnit);

                // If the turn queue is empty, trigger win/lose logic
                if (PlayerArmy.Instance.GetUnitList().Count == 0 || EnemyArmy.Instance.GetUnitList().Count == 0)
                {
                    if(PlayerArmy.Instance.GetUnitList().Count == 0)
                    {
                        EndBattle(false);
                    }
                    else
                    {
                        EndBattle();
                    }

                    return;
                }

                // Handle the case where the dead unit is the last in the queue
                if (currentUnitIndex >= turnQueue.Count)
                {
                    currentUnitIndex = 0; // Wrap around if necessary
                }

                // If the dead unit was the active unit, move to the next one
                if (activeUnitInQueue == deadUnit)
                {
                    currentUnitIndex++;
                    if (currentUnitIndex >= turnQueue.Count)
                    {
                        currentUnitIndex = 0; // Wrap around if necessary
                    }
                }
                else if (deadUnitIndex < currentUnitIndex)
                {
                    currentUnitIndex--; // Adjust the index if the dead unit was earlier in the queue
                }

                // Set the new active unit after adjustments
                SetActiveUnit(turnQueue[currentUnitIndex]);
            }
        }

        /// <summary>
        /// Triggering end battle state in battle manager to continue game flow
        /// </summary>
        private void EndBattle(bool isPlayerWin = true)
        {
            BattleManager.Instance.FinishBattle(false, isPlayerWin);
        }

        /// <summary>
        /// Advances the turn to the next unit in the queue.
        /// Skips dead units and makes sure the correct unit takes its turn.
        /// </summary>
        public void NextTurn()
        {
            // We increase the index by 1
            currentUnitIndex++;

            // Check if the index does not exceed the queue size
            if (currentUnitIndex >= turnQueue.Count)
            {
                currentUnitIndex = 0; // Reset the index to the top of the queue
            }

            // Safety loop: make sure the entity is not dead
            int safetyCounter = 0; // Dodajemy licznik zabezpieczaj¹cy przed nieskoñczon¹ pêtl¹
            while (turnQueue[currentUnitIndex].IsDead())
            {
                currentUnitIndex++;

                if (currentUnitIndex >= turnQueue.Count)
                {
                    currentUnitIndex = 0; // Reset to the top of the list if we have exceeded the list
                }

                safetyCounter++;


                // Infinite loop protection (in case of logic problems)
                if (safetyCounter > turnQueue.Count)
                {
                    Debug.LogError("Cannot find a live unit in the queue!");
                    return;
                }
            }

            isPlayerTurn = !turnQueue[currentUnitIndex].IsEnemy();
            UnitActionSystem.Instance.SetSelectedUnit(turnQueue[currentUnitIndex]);
            //Debug.Log("Now is Player turn: " + isPlayerTurn);

            SetActiveUnit(turnQueue[currentUnitIndex]);
            //Debug.Log("Active unit is now: " + activeUnitInQueue);

            OnTurnChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Initializes the turn queue based on the units' initiative.
        /// </summary>
        private void InitializeTurnQueue()
        {
            turnQueue = new List<Unit>(unitsInBattle);

            // Sort by initiative, and by unit ID as a tie-breaker
            turnQueue.Sort((unit1, unit2) =>
            {
                int initiativeComparison = unit2.GetInitiative().CompareTo(unit1.GetInitiative());
                if (initiativeComparison == 0)
                {
                    return unit1.GetUnitID().CompareTo(unit2.GetUnitID());
                }
                return initiativeComparison;
            });
        }

        #region "Get/Set methods"

        /// <summary>
        /// Sets the active unit for the current turn.
        /// </summary>
        private void SetActiveUnit(Unit unit)
        {
            activeUnitInQueue = unit;
        }


        public int GetCurrentUnitTurnIndex() { return currentUnitIndex; }
        public List<Unit> GetTurnQueue() { return turnQueue; }
        public bool IsPlayerTurn() { return isPlayerTurn; }

        #endregion

    }
}
