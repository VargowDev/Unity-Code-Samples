using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using WarlordsOfArcania.Utilities;
using WarlordsOfArcania.GameSystems;
using WarlordsOfArcania.Units;
using WarlordsOfArcania.UI;
using WarlordsOfArcania.Armies;

namespace WarlordsOfArcania.Actions 
{
    /// <summary>
    /// The SpellCastAction class handles the process of casting a spell, selecting a target, and applying spell effects.
    /// This class transitions through different states such as choosing a spell, targeting, and casting.
    /// It also manages available spells and ensures that valid targets are selected based on the spell type.
    /// </summary>
    public class SpellCastAction : BaseAction
    {
        public event EventHandler OnSpellCastTriggered;

        private enum State { ChoosingSpell, Targeting, Casting }

        [SerializeField] private List<SpellSO> availableSpells;
        private SpellSO selectedSpell;
        private Unit targetUnit;

        private State state = State.ChoosingSpell;
        private float stateTimer;
        private bool canCastSpell = true;
        private bool isTargeting = false;

        private int activeParticleEffects = 0;

        protected override void Awake()
        {
            base.Awake();
            useAllLastActionPoints = true;
        }
        private void OnEnable()
        {
            // Subscribe to the event when the spell is confirmed from the Spellbook UI
            SpellbookWindowUI.Instance.OnSpellCastConfirmed += SpellbookWindowUI_OnSpellCastConfirmed;
            SpellbookWindowUI.Instance.OnActiveSpellChanged += SpellbookWindowUI_OnActiveSpellChanged;
            SetFirstSpellAsActive();
        }
        private void OnDisable()
        {
            // Unsubscribe when the action is disabled
            SpellbookWindowUI.Instance.OnSpellCastConfirmed -= SpellbookWindowUI_OnSpellCastConfirmed;
            SpellbookWindowUI.Instance.OnActiveSpellChanged -= SpellbookWindowUI_OnActiveSpellChanged;
        }
        private void Update()
        {
            // Allow right-click to cancel spell targeting
            if (InputManager.Instance.IsRMBDown() && isTargeting && state == State.Targeting)
            {
                CancelSpellCast();
            }

            if (!isActive) { return; }

            stateTimer -= Time.deltaTime;

            switch (state)
            {
                case State.Targeting:
                    if(selectedSpell.targetType == SpellSO.TargetType.AllPlayerUnits ||
                        selectedSpell.targetType == SpellSO.TargetType.AllEnemyUnits)
                    {
                        NextState();
                    }
                    else if (TrySelectTarget(out targetUnit))
                    {
                        NextState();
                    }
                    break;
                case State.Casting:
                    if (stateTimer <= 0)
                    {
                        if (canCastSpell)
                        {
                            CastSpell();
                            Debug.Log("Casting spell...");
                            canCastSpell = false;
                        }
                    }
                    break;
            }
        }

        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Handles state transitions between spell selection, targeting, and casting.
        /// </summary>
        private void NextState()
        {
            switch (state)
            {
                case State.ChoosingSpell:
                    state = State.Targeting;
                    break;
                case State.Targeting:
                    isTargeting = false;
                    state = State.Casting;
                    stateTimer = 1f;
                    break;
                case State.Casting:
                    ActionComplete();
                    break;
            }
        }

        /// <summary>
        /// Called when player changeing spell in the Spellbook UI.
        /// </summary
        private void SpellbookWindowUI_OnActiveSpellChanged(object sender, SpellSO spell)
        {
            selectedSpell = spell;
        }

        /// <summary>
        /// Called when a spell is selected and confirmed from the Spellbook UI.
        /// </summary>
        private void SpellbookWindowUI_OnSpellCastConfirmed(object sender, SpellSO spell)
        {
            if (unit != UnitActionSystem.Instance.GetSelectedUnit()) return;

            Debug.Log("Chosen spell is: " + spell.spellName);
            isTargeting = true;
            selectedSpell = spell;
            state = State.Targeting;
        }

        /// <summary>
        /// Attempts to select a valid target based on the mouse position and spell target type.
        /// </summary>
        private bool TrySelectTarget(out Unit targetUnit)
        {
            targetUnit = null;

            if(selectedSpell.targetType == SpellSO.TargetType.AllPlayerUnits ||
                selectedSpell.targetType == SpellSO.TargetType.AllEnemyUnits)
            {
                return true;
            }

            GridPosition mouseGridPosition = LevelGrid.Instance.GetGridPosition(MouseWorld.GetWorldMousePosition());

            if (!LevelGrid.Instance.HasAnyUnitAtGridPosition(mouseGridPosition))
            {
                return false;
            }

            targetUnit = LevelGrid.Instance.GetUnitAtGridPosition(mouseGridPosition);

            if (selectedSpell.targetType == SpellSO.TargetType.EnemyUnit && targetUnit.IsEnemy() != unit.IsEnemy())
            {
                return true;
            }
            else if (selectedSpell.targetType == SpellSO.TargetType.PlayerUnit && targetUnit.IsEnemy() == unit.IsEnemy())
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cancels the spell casting process and reopens the spellbook.
        /// </summary>
        private void CancelSpellCast()
        {
            Debug.Log("Cancel spellcasting");
            isTargeting = false;
            ShowSpellBookUI();
            SoundManager.Instance.PlayOnceSpecificClip(SoundManager.Sound.UIClicked, 2);
            state = State.ChoosingSpell;
        }

        /// <summary>
        /// Resets the action state back to spell selection and enables spell casting again.
        /// </summary>
        private void ResetActionState()
        {
            state = State.ChoosingSpell;
            canCastSpell = true;
        }

        /// <summary>
        /// Gets a list of valid grid positions based on the selected spell's range.
        /// </summary>
        public override List<GridPosition> GetValidActionGridPositionList()
        {
            if(selectedSpell.targetType == SpellSO.TargetType.AllEnemyUnits)
            {
                List<GridPosition> validGridPositionsList = new List<GridPosition>();
                List<Unit> unitsToAffect = EnemyArmy.Instance.GetUnitList();

                foreach(Unit unit in unitsToAffect)
                {
                    validGridPositionsList.Add(unit.GetUnitGridPosition());
                }
                return validGridPositionsList;
            }
            else if (selectedSpell.targetType == SpellSO.TargetType.AllPlayerUnits)
            {
                List<GridPosition> validGridPositionsList = new List<GridPosition>();
                List<Unit> unitsToAffect = PlayerArmy.Instance.GetUnitList();

                foreach (Unit unit in unitsToAffect)
                {
                    validGridPositionsList.Add(unit.GetUnitGridPosition());
                }
                return validGridPositionsList;
            }
            else
            {
                GridPosition unitGridPosition = unit.GetUnitGridPosition();
                return GetValidActionGridPositionList(unitGridPosition);
            }
        }
        public List<GridPosition> GetValidActionGridPositionList(GridPosition unitGridPosition)
        {
            List<GridPosition> validGridPositionsList = new List<GridPosition>();
            int maxSpellRange = selectedSpell.spellRange;

            for (int x = -maxSpellRange; x <= maxSpellRange; x++)
            {
                for (int z = -maxSpellRange; z <= maxSpellRange; z++)
                {
                    GridPosition offsetGridPosition = new GridPosition(x, z);
                    GridPosition testGridPosition = unitGridPosition + offsetGridPosition;

                    if (!LevelGrid.Instance.IsValidGridPosition(testGridPosition))
                        continue;

                    int testDistance = Math.Abs(x) + Math.Abs(z);

                    if (testDistance > maxSpellRange)
                        continue;
                    if (!LevelGrid.Instance.HasAnyUnitAtGridPosition(testGridPosition))
                        continue;

                    Unit targetUnit = LevelGrid.Instance.GetUnitAtGridPosition(testGridPosition);

                    if (selectedSpell.targetType == SpellSO.TargetType.EnemyUnit && targetUnit.IsEnemy() != unit.IsEnemy())
                    {
                        validGridPositionsList.Add(testGridPosition);
                    }
                    else if (selectedSpell.targetType == SpellSO.TargetType.PlayerUnit && targetUnit.IsEnemy() == unit.IsEnemy())
                    {
                        validGridPositionsList.Add(testGridPosition);
                    }
                }
            }

            return validGridPositionsList;
        }

        /// <summary>
        /// Initiates the spellcasting process by entering the targeting state.
        /// </summary>
        public override void TakeAction(GridPosition gridPosition, Action onActionComplete)
        {
            ActionStart(onActionComplete);
            OnSpellCastTriggered?.Invoke(this, EventArgs.Empty);
            state = State.Targeting;
        }

        /// <summary>
        /// Opens the spellbook UI.
        /// </summary>
        private void ShowSpellBookUI()
        {
            SpellbookWindowUI.Instance.ShowSpellbook();
        }

        /// <summary>
        /// Sets the first available spell as the active spell.
        /// </summary>
        private void SetFirstSpellAsActive()
        {
            if (availableSpells != null && availableSpells.Count > 0)
            {
                selectedSpell = availableSpells[0];
                Debug.Log("FirstSpell setted!");
            }
            else
            {
                Debug.LogWarning("No available spells on this unit!");
            }
        }

        /// <summary>
        /// Casts the selected spell, applying the appropriate effects to the target.
        /// </summary>
        private void CastSpell()
        {
            switch (selectedSpell.spellType)
            {
                case SpellSO.SpellType.DamageSpell:
                    if (selectedSpell.targetType == SpellSO.TargetType.AllPlayerUnits)
                    {
                        ApplyDamageSpellToAllUnits(PlayerArmy.Instance.GetUnitList());
                    }
                    else if(selectedSpell.targetType == SpellSO.TargetType.AllEnemyUnits)
                    {
                        ApplyDamageSpellToAllUnits(EnemyArmy.Instance.GetUnitList());
                    }
                    else
                    {
                        ApplyDamageSpellToUnit(targetUnit);
                    }
                    break;
                case SpellSO.SpellType.StatModifierSpell:
                    if (selectedSpell.targetType == SpellSO.TargetType.AllPlayerUnits)
                    {
                        ApplyStatModifierSpellToAllUnits(PlayerArmy.Instance.GetUnitList());
                    }
                    else if(selectedSpell.targetType == SpellSO.TargetType.AllEnemyUnits)
                    {
                        ApplyStatModifierSpellToAllUnits(EnemyArmy.Instance.GetUnitList());
                    }
                    else
                    {
                        ApplyStatModifierSpellToUnit(targetUnit);
                    }
                    break;
            }
        }

        private void ApplyDamageSpellToUnit(Unit target)
        {
            GameObject spellParticle = Instantiate(selectedSpell.particlePrefab, target.transform.position, Quaternion.identity);

            StartCoroutine(DamageUnitInDelay(selectedSpell.damageDelay, target, spellParticle));

            activeParticleEffects++;
            StartCoroutine(FadeOutAndDestroyParticle(spellParticle, selectedSpell.particleDuration));
        }
        private void ApplyDamageSpellToAllUnits(List<Unit> units)
        {
            List<Unit> unitsToAffect = units;

            foreach (Unit target in unitsToAffect)
            {
                ApplyDamageSpellToUnit(target);
            }
        }
        private void ApplyStatModifierSpellToUnit(Unit target)
        {
            foreach (UnitStatModifier modifier in selectedSpell.spellEffects)
            {
                GameObject spellParticle = Instantiate(selectedSpell.particlePrefab, target.transform.position, Quaternion.identity);

                target.GetUnitStats().AddModifier(modifier);

                activeParticleEffects++;

                StartCoroutine(FadeOutAndDestroyParticle(spellParticle, selectedSpell.particleDuration));

                if (selectedSpell.spellSFX != null)
                {
                    AudioSource audioSource = spellParticle.AddComponent<AudioSource>();
                    audioSource.spatialBlend = 1f;
                    audioSource.clip = selectedSpell.spellSFX;
                    audioSource.volume = 1f;
                    audioSource.loop = false;
                    audioSource.Play();
                }
            }
        }
        private void ApplyStatModifierSpellToAllUnits(List<Unit> units)
        {
            List<Unit> unitsToAffect = units;

            foreach (Unit target in unitsToAffect)
            {
                ApplyStatModifierSpellToUnit(target);
            }
        }

        private IEnumerator DamageUnitInDelay(float delay, Unit target, GameObject particle)
        {
            yield return new WaitForSeconds(delay);

            if(selectedSpell.spellSFX != null)
            {
                AudioSource audioSource = particle.AddComponent<AudioSource>();
                audioSource.spatialBlend = 1f;
                audioSource.clip = selectedSpell.spellSFX;
                audioSource.volume = 1f;
                audioSource.loop = false;
                audioSource.Play();
            }

            int physicalDamageAmount = UnityEngine.Random.Range(selectedSpell.minPhysicalDamage, selectedSpell.maxPhysicalDamage);
            int magicalDamageAmount = UnityEngine.Random.Range(selectedSpell.minMagicalDamage, selectedSpell.maxMagicalDamage);

            target.Damage(physicalDamageAmount, magicalDamageAmount);

            unit.GetUnitStats().GainExperience(physicalDamageAmount + magicalDamageAmount);
        }
        private IEnumerator FadeOutAndDestroyParticle(GameObject particleObject, float duration)
        {
            yield return new WaitForSeconds(duration);

            ParticleSystem[] particleSystems = particleObject.GetComponentsInChildren<ParticleSystem>();

            if (particleSystems.Length > 0)
            {
                foreach (ParticleSystem ps in particleSystems)
                    ps.Stop();

                float maxLifetime = 0f;
                foreach (ParticleSystem ps in particleSystems)
                    maxLifetime = Mathf.Max(maxLifetime, ps.main.startLifetime.constantMax);

                yield return new WaitForSeconds(maxLifetime);
                Destroy(particleObject);
            }
            else
            {
                Destroy(particleObject);
            }

            activeParticleEffects--;

            if (activeParticleEffects <= 0)
            {
                activeParticleEffects = 0;

                OnActionComplete();
            }
        }

        /// <summary>
        /// Completes the action and resets the state.
        /// </summary>
        private void OnActionComplete()
        {
            ActionComplete();
            ResetActionState();
            isTargeting = false;
            UnitActionSystem.Instance.SetSelectedAction(unit.GetAction<MoveAction>());
        }
        public override EnemyAIAction GetEnemyAIAction(GridPosition gridPosition)
        {
            return new EnemyAIAction()
            {
                gridPosition = gridPosition,
                actionValue = 1,
            };
        }

        #region Get/Set Methods

        public override string GetActionName() => "Spellbook";
        public List<SpellSO> GetAvailableSpells() => availableSpells;
        public SpellSO GetActiveSpell() => selectedSpell;
        public int GetMaxSpellRange() => selectedSpell.spellRange;

        #endregion
    }
}

