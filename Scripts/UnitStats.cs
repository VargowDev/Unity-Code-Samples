using System;
using System.Collections.Generic;
using UnityEngine;
using WarlordsOfArcania.GameSystems;

namespace WarlordsOfArcania.Units
{
    /// <summary>
    /// Handles unit statistics, including health, damage, armor, and experience.
    /// Supports adding and updating stat modifiers, managing experience progression,
    /// and handling unit death and damage.
    /// </summary>
    public class UnitStats : MonoBehaviour
    {
        public int unitID { get; private set; }

        public event EventHandler OnDead;
        public event Action<int, int> OnDamageTaken;

        private Dictionary<StatType, List<UnitStatModifier>> activeModifiers;
        private Dictionary<StatType, int> originalStatValues;

        [SerializeField] private UnitTypeSO unitType;
        private Unit unit;

        [Header("Base Information")]
        public int level;
        public int initiative;
        public int actionPoints;

        [Header("Experience Information")]
        public int currentExperience;
        public int experienceToNextLevel;

        [Header("Health Information")]
        public int maxHealth;
        public int currentHealth;

        [Header("Defense Information")]
        public int armor;
        public int magicArmor;

        [Header("Attack Information")]
        public int minPhysicalDamage;
        public int maxPhysicalDamage;
        public int minMagicalDamage;
        public int maxMagicalDamage;

        /// <summary>
        /// Initializes the unit's stats using the provided UnitTypeSO and unit ID.
        /// </summary>
        public void Initialize(UnitTypeSO unitType, int unitID)
        {
            this.unitType = unitType;
            this.unitID = unitID;
            activeModifiers = new Dictionary<StatType, List<UnitStatModifier>>();
            SubscribeUnitActionSystem();
        }

        /// <summary>
        /// Event handler for when the unit's turn ends. Updates active modifiers.
        /// </summary>
        private void UnitActionSystem_OnUnitTurnEnd(object sender, EventArgs e)
        {
            if (unit.IsThisUnitTurn())
            {
                UpdateModifiers();
            }
        }

        /// <summary>
        /// Subscribes this unit to the UnitActionSystem's OnUnitTurnEnd event.
        /// </summary>
        public void SubscribeUnitActionSystem()
        {
            UnitActionSystem.Instance.OnUnitTurnEnd += UnitActionSystem_OnUnitTurnEnd;
        }

        #region Get/Set Methods

        public string GetUnitName() { return unitType.unitName; }
        public Unit GetUnit() { return unit; }
        public UnitTypeSO GetUnitType() { return unitType; }
        public void SetUnit(Unit unit) { this.unit = unit; }
        public string GetHealthAmountWithTotal() { return $"{currentHealth}/{maxHealth}"; }
        public float GetHealthNormalized() { return (float)currentHealth / maxHealth; }

        #endregion

        #region Damage Methods

        /// <summary>
        /// Deals damage to the unit, calculating physical and magical damage after applying armor reductions.
        /// </summary>
        /// <param name="physicalDamageAmount">Amount of physical damage to apply.</param>
        /// <param name="magicalDamageAmount">Amount of magical damage to apply.</param>
        public void DealDamage(int physicalDamageAmount, int magicalDamageAmount)
        {
            int finalPhysicalDamage = 0;
            int finalMagicalDamage = 0;

            // Check if the unit deals physical damage
            if (physicalDamageAmount > 0)
                finalPhysicalDamage = DamageAfterArmor(physicalDamageAmount, armor);

            // Check if the unit deals magical damage
            if (magicalDamageAmount > 0)
                finalMagicalDamage = DamageAfterArmor(magicalDamageAmount, magicArmor);

            int totalDamage = finalPhysicalDamage + finalMagicalDamage;

            // Apply the total damage to current health
            currentHealth -= totalDamage;
            if (currentHealth < 0)
                currentHealth = 0;
             
            OnDamageTaken?.Invoke(finalPhysicalDamage, finalMagicalDamage);

            if (currentHealth == 0)
                Die();
        }

        /// <summary>
        /// Calculates the damage amount after applying armor reduction.
        /// </summary>
        /// <param name="damageAmount">Base damage amount.</param>
        /// <param name="armor">Armor value to reduce the damage.</param>
        /// <returns>The final damage amount after reduction.</returns>
        private int DamageAfterArmor(int damageAmount, int armor)
        {
            const float armorConstant = 10.0f;

            float damageModifier = 1 - (armor / (armor + armorConstant));
            int damageAfterArmor = Mathf.RoundToInt(damageAmount * damageModifier);
            int minimumDamage = Mathf.RoundToInt(damageAmount * 0.3f);
            int finalDamage = Mathf.Max(damageAfterArmor, minimumDamage);

            return finalDamage;
        }

        /// <summary>
        /// Handles unit death, triggering the OnDead event and updating visuals.
        /// </summary>
        private void Die()
        {
            OnDead?.Invoke(this, EventArgs.Empty);
            GridSystemVisual.Instance.UpdateGridVisualForAction();
        }

        /// <summary>
        /// Generates random physical and magical damage amounts within the unit's damage range.
        /// </summary>
        /// <returns>A tuple containing physical and magical damage amounts.</returns>
        public (int physicalDamage, int magicalDamage) GenerateRandomDamage()
        {
            int physicalDamageAmount = 0;
            int magicalDamageAmount = 0;

            // Generate physical damage if the unit deals physical damage
            if (minPhysicalDamage > 0 || maxPhysicalDamage > 0)
                physicalDamageAmount = UnityEngine.Random.Range(minPhysicalDamage, maxPhysicalDamage + 1);

            // Generate magical damage if the unit deals magical damage
            if (minMagicalDamage > 0 || maxMagicalDamage > 0)
                magicalDamageAmount = UnityEngine.Random.Range(minMagicalDamage, maxMagicalDamage + 1);

            return (physicalDamageAmount, magicalDamageAmount);
        }

        #endregion

        #region Modifiers Methods

        /// <summary>
        /// Adds a modifier to the unit's stats and updates active modifiers.
        /// </summary>
        /// <param name="modifier">The stat modifier to add.</param>
        public void AddModifier(UnitStatModifier modifier)
        {
            if (!activeModifiers.ContainsKey(modifier.modifiedStat))
            {
                activeModifiers[modifier.modifiedStat] = new List<UnitStatModifier>();
            }
            modifier.isAlreadyActive = false;

            activeModifiers[modifier.modifiedStat].Add(modifier);
        }

        /// <summary>
        /// Adjusts a specific unit stat based on the given modifier by applying a multiplier to its original value.
        /// </summary>
        /// <param name="modifier">The stat modifier to apply on stats</param>
        private void ApplyModifier(UnitStatModifier modifier)
        {
            float modifierMultiplier = 1 + (modifier.modifierValue / 100f);

            switch (modifier.modifiedStat)
            {
                case StatType.Health:
                    maxHealth = Mathf.RoundToInt(originalStatValues[StatType.Health] * modifierMultiplier);
                    currentHealth = Mathf.Min(currentHealth, maxHealth);
                    Debug.Log("Now Health is: " + currentHealth + " for " + unit.GetUnitName());
                    break;
                case StatType.Armor:
                    armor = Mathf.RoundToInt(originalStatValues[StatType.Armor] * modifierMultiplier);
                    Debug.Log("Now Armor is: " + armor + " for " + unit.GetUnitName());
                    break;
                case StatType.MagicArmor:
                    magicArmor = Mathf.RoundToInt(originalStatValues[StatType.MagicArmor] * modifierMultiplier);
                    Debug.Log("Now Magic Armor is: " + magicArmor + " for " + unit.GetUnitName());
                    break;
                case StatType.MinPhysicalDamage:
                    minPhysicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MinPhysicalDamage] * modifierMultiplier);
                    Debug.Log("Now Minimum Physical Damage is: " + minPhysicalDamage + " for " + unit.GetUnitName());
                    break;
                case StatType.MaxPhysicalDamage:
                    maxPhysicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MaxPhysicalDamage] * modifierMultiplier);
                    Debug.Log("Now Maximum Physical Damage is: " + maxPhysicalDamage + " for " + unit.GetUnitName());
                    break;
                case StatType.MinMagicalDamage:
                    minMagicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MinMagicalDamage] * modifierMultiplier);
                    Debug.Log("Now Minimum Magical Damage is: " + minMagicalDamage + " for " + unit.GetUnitName());
                    break;
                case StatType.MaxMagicalDamage:
                    maxMagicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MaxMagicalDamage] * modifierMultiplier);
                    Debug.Log("Now Maximum Magical Damage is: " + maxMagicalDamage + " for " + unit.GetUnitName());
                    break;
                case StatType.ActionPoints:
                    actionPoints = Mathf.RoundToInt(originalStatValues[StatType.ActionPoints] * modifierMultiplier);
                    Debug.Log("Now Amount of AP is: " + actionPoints + " for " + unit.GetUnitName());
                    break;
                case StatType.Initiative:
                    initiative = Mathf.RoundToInt(originalStatValues[StatType.Initiative] * modifierMultiplier);
                    Debug.Log("Initiative now is: " + initiative + " for " + unit.GetUnitName());
                    break;
            }

            ClampStatsToMinimumValues();
        }

        /// <summary>
        /// Ensures all stats meet their minimum allowable values.
        /// </summary>
        private void ClampStatsToMinimumValues()
        {
            maxHealth = Mathf.Max(maxHealth, 1);
            currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);

            armor = Mathf.Max(armor, 0);
            magicArmor = Mathf.Max(magicArmor, 0);

            minPhysicalDamage = Mathf.Max(minPhysicalDamage, 0);
            maxPhysicalDamage = Mathf.Max(maxPhysicalDamage, minPhysicalDamage);

            minMagicalDamage = Mathf.Max(minMagicalDamage, 0);
            maxMagicalDamage = Mathf.Max(maxMagicalDamage, minMagicalDamage);

            actionPoints = Mathf.Max(actionPoints, 1);
            initiative = Mathf.Max(initiative, 1);
        }

        /// <summary>
        /// Removes a stat modifier, restoring the original stat value.
        /// </summary>
        /// <param name="modifier">The stat modifier to remove.</param>
        private void RemoveModifier(UnitStatModifier modifier)
        {
            if (originalStatValues.ContainsKey(modifier.modifiedStat))
            {
                switch (modifier.modifiedStat)
                {
                    case StatType.Health:
                        maxHealth = originalStatValues[modifier.modifiedStat];
                        currentHealth = Mathf.Min(currentHealth, maxHealth);
                        break;
                    case StatType.Armor:
                        armor = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.MagicArmor:
                        magicArmor = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.MinPhysicalDamage:
                        minPhysicalDamage = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.MaxPhysicalDamage:
                        maxPhysicalDamage = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.MinMagicalDamage:
                        minMagicalDamage = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.MaxMagicalDamage:
                        maxMagicalDamage = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.ActionPoints:
                        actionPoints = originalStatValues[modifier.modifiedStat];
                        break;
                    case StatType.Initiative:
                        initiative = originalStatValues[modifier.modifiedStat];
                        break;
                }
            }
        }

        /// <summary>
        /// Calculates the total modifier value for a specific stat type by summing the percentage modifiers from all active modifiers.
        /// </summary>
        /// <param name="statType">Stat for witch we want Modifiers</param>
        /// <returns></returns>
        public int GetModifierValue(StatType statType)
        {
            float totalMultiplier = 0f;

            if (activeModifiers.ContainsKey(statType))
            {
                List<UnitStatModifier> modifiers = activeModifiers[statType];
                foreach (var modifier in modifiers)
                {
                    totalMultiplier += (modifier.modifierValue / 100f);
                }
            }

            int baseValue = GetOriginalStatValue(statType);
            int finalModifierValue = Mathf.RoundToInt(baseValue * totalMultiplier);

            return finalModifierValue;
        }

        /// <summary>
        /// Retrieves the original, unmodified value of the specified stat type
        /// </summary>
        /// <param name="statType">Stat type</param>
        /// <returns></returns>
        public int GetOriginalStatValue(StatType statType)
        {
            return originalStatValues[statType];
        }

        /// <summary>
        /// Saves the base values of all unit stats
        /// </summary>
        public void SaveOriginalStatValues()
        {
            originalStatValues = new Dictionary<StatType, int>();

            originalStatValues[StatType.Health] = maxHealth;
            originalStatValues[StatType.Armor] = armor;
            originalStatValues[StatType.MagicArmor] = magicArmor;
            originalStatValues[StatType.MinPhysicalDamage] = minPhysicalDamage;
            originalStatValues[StatType.MaxPhysicalDamage] = maxPhysicalDamage;
            originalStatValues[StatType.MinMagicalDamage] = minMagicalDamage;
            originalStatValues[StatType.MaxMagicalDamage] = maxMagicalDamage;
            originalStatValues[StatType.ActionPoints] = actionPoints;
            originalStatValues[StatType.Initiative] = initiative;
        }

        /// <summary>
        /// Updates all active stat modifiers, applying their effects and handling expiration.
        /// </summary>
        public void UpdateModifiers()
        {
            foreach (var modifierList in activeModifiers.Values)
            {
                for (int i = 0; i < modifierList.Count; i++)
                {
                    UnitStatModifier modifier = modifierList[i];
                    if (!modifier.isAlreadyActive)
                    {
                        ApplyModifier(modifier);
                        modifier.isAlreadyActive = true;
                        modifierList[i] = modifier;
                    }

                    if (!modifier.isPermanent)
                    {
                        modifier.DecreaseDuration();
                        Debug.Log($"Modifier {modifier.modifiedStat} new duration: {modifier.duration}");

                        if (modifier.duration <= 0)
                        {
                            Debug.Log($"Removing modifier for {modifier.modifiedStat}");
                            RemoveModifier(modifierList[i]);
                            modifierList.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }
        }

        #endregion

        #region Experience Methods

        /// <summary>
        /// Increases the unit's experience by the specified amount. Triggers a level-up if the threshold is exceeded.
        /// </summary>
        /// <param name="amount">The amount of experience gained.</param>
        public void GainExperience(int amount)
        {
            int xp = Mathf.CeilToInt((float)amount / 2);
            currentExperience += xp;
            if(currentExperience >= experienceToNextLevel)
            {
                LevelUp();
            }
        }

        /// <summary>
        /// Handles the unit leveling up, increasing stats and recalculating the experience threshold.
        /// </summary>
        private void LevelUp()
        {
            level++;
            experienceToNextLevel += CalculateNextLevelExperience();
            IncreaseStats();

            Debug.Log($"{GetUnitName()} leveled up to {level}!");
        }

        /// <summary>
        /// Increases the unit's stats upon leveling up.
        /// </summary>
        private void IncreaseStats()
        {
            maxHealth = Mathf.RoundToInt(originalStatValues[StatType.Health] * 1.15f);
            currentHealth = Mathf.RoundToInt(currentHealth * 1.15f);

            minPhysicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MinPhysicalDamage] * 1.15f);
            maxPhysicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MaxPhysicalDamage] * 1.15f);
            minMagicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MinMagicalDamage] * 1.15f);
            maxMagicalDamage = Mathf.RoundToInt(originalStatValues[StatType.MaxMagicalDamage] * 1.15f);

            armor = Mathf.RoundToInt(originalStatValues[StatType.Armor] * 1.1f);
            magicArmor = Mathf.RoundToInt(originalStatValues[StatType.MagicArmor] * 1.1f);

            originalStatValues[StatType.Health] = maxHealth;
            originalStatValues[StatType.MinPhysicalDamage] = minPhysicalDamage;
            originalStatValues[StatType.MaxPhysicalDamage] = maxPhysicalDamage;
            originalStatValues[StatType.MinMagicalDamage] = minMagicalDamage;
            originalStatValues[StatType.MaxMagicalDamage] = maxMagicalDamage;
            originalStatValues[StatType.Armor] = armor;
            originalStatValues[StatType.MagicArmor] = magicArmor;
        }

        /// <summary>
        /// Calculates the experience needed for the next level.
        /// </summary>
        /// <returns>The experience required for the next level.</returns>
        private int CalculateNextLevelExperience()
        {
            return Mathf.RoundToInt(experienceToNextLevel * 1.5f);
        }

        #endregion
    }
}

