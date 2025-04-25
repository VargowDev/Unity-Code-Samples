using UnityEngine;
using StarterAssets;
using TheRevenantEngine.UI;
using System.Collections.Generic;
using TheRevenantEngine.Health;
using System;
using TheRevenantEngine.Systems;

namespace TheRevenantEngine.Equipment
{
    /// <summary>
    /// Manages the currently equipped weapons, shooting logic, weapon switching, and reloading for the player.
    /// Integrates with input, UI, and equipment systems. Supports both 
    /// automatic and semi-automatic firearms with state-based firing and reloading behavior
    /// </summary>
    public class ActiveWeaponHandler : MonoBehaviour
    {
        private StarterAssetsInputs starterAssetsInputs;

        public Weapon activeWeapon;
        public RangedWeapon currentRangedWeapon;

        private GameObject primaryWeaponGO;
        private Weapon primaryWeapon;

        private GameObject secondaryWeaponGO;
        private Weapon secondaryWeapon;

        private bool isHoldingFire = false;

        [Header("Weapon Types")]
        [SerializeField] GameObject AKWeaponPrefab;
        [SerializeField] GameObject RevolverWeaponPrefab;
        [SerializeField] GameObject PistolWeaponPrefab;

        private Dictionary<WeaponClass, GameObject> weaponPrefabs;

        private bool isAnyWindowOpen = false;

        public delegate void OnWeaponChanged();
        public event OnWeaponChanged WeaponChanged;

        private void Awake()
        {
            starterAssetsInputs = GetComponentInParent<StarterAssetsInputs>();

            weaponPrefabs = new Dictionary<WeaponClass, GameObject>
            {
                { WeaponClass.AK, AKWeaponPrefab },
                { WeaponClass.Revolver, RevolverWeaponPrefab },
                { WeaponClass.Pistol, PistolWeaponPrefab }
            };

            InventoryUI.OnInventoryToggle += InventoryUI_OnInventoryToggle;
            PlayerEquipment.Instance.EquipmentChanged += PlayerEquipment_OnEquipmentChanged;
            PlayerHealth.OnPlayerDied += PlayerHealth_OnPlayerDied;
            GameManager.OnLevelFinished += GameManager_OnLevelFinished;
        }
        private void Start()
        {
            DeactivateAllWeapons();
        }
        private void Update()
        {
            if (isAnyWindowOpen)
                return;

            ProcessShootInput();
            ProcessWeaponSwitchInput();
            ProcessReloadInput();
        }
        private void OnDestroy()
        {
            InventoryUI.OnInventoryToggle -= InventoryUI_OnInventoryToggle;
            PlayerEquipment.Instance.EquipmentChanged -= PlayerEquipment_OnEquipmentChanged;
            PlayerHealth.OnPlayerDied -= PlayerHealth_OnPlayerDied;
            GameManager.OnLevelFinished -= GameManager_OnLevelFinished;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        #region Input Processing Methods

        /// <summary>
        /// Processes the player's shooting input, updating the isHoldingFire flag and triggering the HandleShooting logic.
        /// </summary>
        private void ProcessShootInput()
        {
            if (starterAssetsInputs.shoot)
            {
                isHoldingFire = true;
                HandleShooting();
                starterAssetsInputs.ShootInput(false);
            }
            if (!starterAssetsInputs.shoot)
            {
                isHoldingFire = false;
            }

            HandleShooting();
        }

        /// <summary>
        /// Processes player input for switching between primary/secondary weapons or using the dedicated swap button.
        /// </summary>
        private void ProcessWeaponSwitchInput()
        {
            if (starterAssetsInputs.swapWeapon)
            {
                if (activeWeapon != null && activeWeapon.weaponState.currentState != WeaponStateType.Disabled)
                    SwapWeapons();
                starterAssetsInputs.WeaponSwapInput(false);
            }

            if (starterAssetsInputs.primaryWeapon)
            {
                if (activeWeapon == null || (activeWeapon != primaryWeapon && activeWeapon.weaponState.currentState != WeaponStateType.Disabled))
                    SelectPrimaryWeapon();
                starterAssetsInputs.PrimaryWeaponInput(false);
            }

            if (starterAssetsInputs.secondaryWeapon)
            {
                if (activeWeapon == null || (activeWeapon != secondaryWeapon && activeWeapon.weaponState.currentState != WeaponStateType.Disabled))
                    SelectSecondaryWeapon();
                starterAssetsInputs.SecondaryWeaponInput(false);
            }
        }

        /// <summary>
        /// Processes player input for reloading the currently active ranged weapon if conditions are met.
        /// </summary>
        private void ProcessReloadInput()
        {
            if (starterAssetsInputs.reload)
            {
                if (activeWeapon != null && currentRangedWeapon != null
                    && activeWeapon.weaponState.currentState == WeaponStateType.Idle
                    && currentRangedWeapon.GetCurrentAmmoAmount() < currentRangedWeapon.GetMaxAmmoAmount()
                    && currentRangedWeapon.GetAvailableAmmo() > 0)
                {
                    activeWeapon.weaponState.ChangeState(WeaponStateType.Reloading);
                }
                starterAssetsInputs.ReloadInput(false);
            }
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles the event triggered when the level is finished, deactivating all weapons.
        /// </summary>
        private void GameManager_OnLevelFinished()
        {
            DeactivateAllWeapons();
        }

        /// <summary>
        /// Handles the event triggered when the player dies, deactivating all weapons.
        /// </summary>
        private void PlayerHealth_OnPlayerDied()
        {
            DeactivateAllWeapons();
        }

        /// <summary>
        /// Handles the event triggered when the player's equipment changes. Updates the primary and secondary weapon references and GameObjects based on the new equipment state.
        /// </summary>
        private void PlayerEquipment_OnEquipmentChanged()
        {
            ItemEntry primaryEntry = PlayerEquipment.Instance.GetEquippedItem(EquipmentSlotType.PrimaryWeapon);
            ItemEntry secondaryEntry = PlayerEquipment.Instance.GetEquippedItem(EquipmentSlotType.SecondaryWeapon);

            if (primaryEntry != null)
            {
                if (primaryWeaponGO != null)
                {
                    WeaponClass currentType = primaryWeapon.weaponTypeData.weaponClass;
                    if (currentType != primaryEntry.itemData.weaponType.weaponClass)
                    {
                        primaryWeaponGO.SetActive(false);
                        primaryWeaponGO = null;
                    }
                }

                if (primaryWeaponGO == null)
                {
                    if (weaponPrefabs.TryGetValue(primaryEntry.itemData.weaponType.weaponClass, out GameObject prefab))
                    {
                        primaryWeaponGO = prefab.gameObject;
                        primaryWeapon = primaryWeaponGO.GetComponent<Weapon>();

                        if (activeWeapon == null)
                        {
                            primaryWeaponGO.SetActive(true);
                            SetActiveWeapon(primaryWeapon);
                        }
                        else
                        {
                            primaryWeaponGO.SetActive(false);
                        }
                    }
                }

                if (primaryWeapon is RangedWeapon primaryRanged)
                {
                    var ammoItem = PlayerEquipment.Instance.GetEquippedItem(EquipmentSlotType.PrimaryAmmo);

                    if (ammoItem != null && primaryWeapon.weaponTypeData.IsThisCorrectAmmoType(ammoItem.itemData.ammoType))
                    {
                        primaryRanged.SetupAmmoItem(ammoItem);
                        PlayerAmmoUI.Instance.UpdateAmmoAmount(currentRangedWeapon.GetCurrentAmmoAmount(), currentRangedWeapon.GetAvailableAmmo());
                    }
                    else
                    {
                        primaryRanged.SetupAmmoItem(null);
                        PlayerAmmoUI.Instance.UpdateAmmoAmount(currentRangedWeapon.GetCurrentAmmoAmount(), currentRangedWeapon.GetAvailableAmmo());
                    }
                }
            }
            else
            {
                if (primaryWeaponGO != null)
                {
                    if (activeWeapon == primaryWeapon)
                        activeWeapon = null;

                    primaryWeaponGO.SetActive(false);
                    primaryWeaponGO = null;
                    primaryWeapon = null;
                }
            }

            if (secondaryEntry != null)
            {
                if (secondaryWeaponGO != null)
                {
                    WeaponClass currentType = secondaryWeapon.weaponTypeData.weaponClass;
                    if (currentType != secondaryEntry.itemData.weaponType.weaponClass)
                    {
                        secondaryWeaponGO.SetActive(false);
                        secondaryWeaponGO = null;
                    }
                }

                if (secondaryWeaponGO == null)
                {
                    if (weaponPrefabs.TryGetValue(secondaryEntry.itemData.weaponType.weaponClass, out GameObject prefab))
                    {
                        secondaryWeaponGO = prefab.gameObject;
                        secondaryWeapon = secondaryWeaponGO.GetComponent<Weapon>();

                        if (activeWeapon == null)
                        {
                            secondaryWeaponGO.SetActive(true);
                            SetActiveWeapon(secondaryWeapon);
                        }
                        else
                        {
                            secondaryWeaponGO.SetActive(false);
                        }
                    }
                }

                if (secondaryWeapon is RangedWeapon secondaryRanged)
                {
                    var ammoItem = PlayerEquipment.Instance.GetEquippedItem(EquipmentSlotType.SecondaryAmmo);
                    if (ammoItem != null && secondaryWeapon.weaponTypeData.IsThisCorrectAmmoType(ammoItem.itemData.ammoType))
                    {
                        secondaryRanged.SetupAmmoItem(ammoItem);
                        PlayerAmmoUI.Instance.UpdateAmmoAmount(currentRangedWeapon.GetCurrentAmmoAmount(), currentRangedWeapon.GetAvailableAmmo());
                    }
                    else
                    {
                        secondaryRanged.SetupAmmoItem(null);
                        PlayerAmmoUI.Instance.UpdateAmmoAmount(currentRangedWeapon.GetCurrentAmmoAmount(), currentRangedWeapon.GetAvailableAmmo());
                    }
                }
            }
            else
            {
                if (secondaryWeaponGO != null)
                {
                    if (activeWeapon == secondaryWeapon)
                        activeWeapon = null;

                    secondaryWeaponGO.SetActive(false);
                    secondaryWeaponGO = null;
                    secondaryWeapon = null;
                }
            }
            if (activeWeapon == null)
            {
                if (primaryWeapon != null)
                {
                    primaryWeaponGO.SetActive(true);
                    SetActiveWeapon(primaryWeapon);
                }
                else if (secondaryWeapon != null)
                {
                    secondaryWeaponGO.SetActive(true);
                    SetActiveWeapon(secondaryWeapon);
                }
                else
                {
                    PlayerAmmoUI.Instance.UpdateAmmoAmount(0, 0);
                }
            }
        }

        /// <summary>
        /// Handles the event triggered when the inventory UI is toggled open or closed.
        /// </summary>
        /// <param name="isOpen">True if the inventory is now open, false otherwise.</param>
        private void InventoryUI_OnInventoryToggle(bool obj)
        {
            isAnyWindowOpen = obj;
        }

        #endregion

        #region Weapon Management Methods

        /// <summary>
        /// Sets the provided weapon as the currently active weapon, handling deactivation of the previous one and updating UI/event subscriptions.
        /// </summary>
        /// <param name="newWeapon">The Weapon component to set as active.</param>
        private void SetActiveWeapon(Weapon newWeapon)
        {
            if (activeWeapon != null && activeWeapon != newWeapon)
            {
                activeWeapon.weaponState.InterruptAction();

                if (activeWeapon is RangedWeapon oldRanged)
                {
                    oldRanged.OnAmmoChanged -= PlayerAmmoUI.Instance.UpdateAmmoAmount;
                }
            }

            activeWeapon = newWeapon;
            currentRangedWeapon = activeWeapon as RangedWeapon;

            if (currentRangedWeapon != null)
            {
                currentRangedWeapon.OnAmmoChanged += PlayerAmmoUI.Instance.UpdateAmmoAmount;
                PlayerAmmoUI.Instance.UpdateAmmoAmount(currentRangedWeapon.currentAmmo, currentRangedWeapon.availableAmmo);
            }
            else
            {
                PlayerAmmoUI.Instance.UpdateAmmoAmount(0, 0);
            }

            WeaponChanged?.Invoke();
        }

        /// <summary>
        /// Attempts to fire the active weapon based on its type (automatic/semi-automatic), ammo count, fire rate, and player input state. Handles automatic reloading on empty clip attempt.
        /// </summary>
        private void HandleShooting()
        {
            if (activeWeapon != null && activeWeapon.weaponState.currentState == WeaponStateType.Reloading)
                return;

            if (starterAssetsInputs.sprint)
                return;

            if (activeWeapon is RangedWeapon rangedWeapon)
            {
                if (!rangedWeapon.isAutomatic)
                {
                    if (isHoldingFire && Time.time >= rangedWeapon.nextAllowedFireTime)
                    {
                        if (rangedWeapon.currentAmmo <= 0)
                        {
                            if (currentRangedWeapon.GetCurrentAmmoAmount() == currentRangedWeapon.GetMaxAmmoAmount()
                                || currentRangedWeapon.GetAvailableAmmo() <= 0)
                            {
                                return;
                            }

                            activeWeapon.weaponState.ChangeState(WeaponStateType.Reloading);
                        }
                        else
                        {
                            activeWeapon.weaponState.ChangeState(WeaponStateType.Firing);
                            rangedWeapon.nextAllowedFireTime = Time.time + (1f / rangedWeapon.fireRate);
                        }

                        isHoldingFire = false;
                    }
                }
                else
                {
                    if (isHoldingFire && Time.time >= rangedWeapon.nextAllowedFireTime)
                    {
                        if (rangedWeapon.currentAmmo <= 0)
                        {
                            if (currentRangedWeapon.GetCurrentAmmoAmount() == currentRangedWeapon.GetMaxAmmoAmount()
                                || currentRangedWeapon.GetAvailableAmmo() <= 0)
                            {
                                return;
                            }
                            activeWeapon.weaponState.ChangeState(WeaponStateType.Reloading);
                            isHoldingFire = false; 
                        }
                        else
                        {
                            activeWeapon.weaponState.ChangeState(WeaponStateType.Firing);
                            rangedWeapon.nextAllowedFireTime = Time.time + (1f / rangedWeapon.fireRate);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Swaps the active weapon between the primary and secondary slots if both are equipped.
        /// </summary>
        private void SwapWeapons()
        {
            if (primaryWeapon == null || secondaryWeapon == null || primaryWeapon == secondaryWeapon)
                return;

            Weapon weaponToDeactivate = null;
            Weapon weaponToActivate = null;
            GameObject go_ToDeactivate = null;
            GameObject go_ToActivate = null;

            if (activeWeapon == primaryWeapon)
            {
                weaponToDeactivate = primaryWeapon;
                go_ToDeactivate = primaryWeaponGO;
                weaponToActivate = secondaryWeapon;
                go_ToActivate = secondaryWeaponGO;
            }
            else if (activeWeapon == secondaryWeapon)
            {
                weaponToDeactivate = secondaryWeapon;
                go_ToDeactivate = secondaryWeaponGO;
                weaponToActivate = primaryWeapon;
                go_ToActivate = primaryWeaponGO;
            }
            else
            {
                //Debug.LogWarning("SwapWeapons called but activeWeapon is neither primary nor secondary.");
                if (primaryWeapon != null)
                {
                    SelectPrimaryWeapon();
                }
                else if (secondaryWeapon != null)
                {
                    SelectSecondaryWeapon();
                }

                return;
            }

            SetActiveWeapon(weaponToActivate);

            if (go_ToDeactivate != null) 
                go_ToDeactivate.SetActive(false);
            if (go_ToActivate != null) 
                go_ToActivate.SetActive(true);
        }

        /// <summary>
        /// Selects the primary weapon as the active weapon, deactivating the secondary if necessary.
        /// </summary>
        private void SelectPrimaryWeapon()
        {
            if (primaryWeapon == null || activeWeapon == primaryWeapon)
                return;

            Weapon weaponToDeactivate = activeWeapon;
            GameObject go_ToDeactivate = (weaponToDeactivate == secondaryWeapon) ? secondaryWeaponGO : null;

            SetActiveWeapon(primaryWeapon);

            if (go_ToDeactivate != null && go_ToDeactivate != primaryWeaponGO) 
                go_ToDeactivate.SetActive(false);

            if (primaryWeaponGO != null)
                primaryWeaponGO.SetActive(true);
        }

        /// <summary>
        /// Selects the secondary weapon as the active weapon, deactivating the primary if necessary.
        /// </summary>
        private void SelectSecondaryWeapon()
        {
            if (secondaryWeapon == null || activeWeapon == secondaryWeapon)
                return;

            Weapon weaponToDeactivate = activeWeapon;
            GameObject go_ToDeactivate = (weaponToDeactivate == primaryWeapon) ? primaryWeaponGO : null;

            SetActiveWeapon(secondaryWeapon);

            if (go_ToDeactivate != null && go_ToDeactivate != secondaryWeaponGO)
                go_ToDeactivate.SetActive(false);

            if (secondaryWeaponGO != null) 
                secondaryWeaponGO.SetActive(true);

        }

        /// <summary>
        /// Deactivates the GameObjects associated with all weapons defined in the weaponPrefabs dictionary and clears the active weapon reference.
        /// </summary>
        private void DeactivateAllWeapons()
        {
            foreach (var kvp in weaponPrefabs)
            {
                if (kvp.Value != null)
                    kvp.Value.SetActive(false);
            }
            activeWeapon = null;
        }

        #endregion

        #region Getters

        public Weapon GetActiveWeapon() => activeWeapon;
        public Weapon GetPrimaryWeapon() => primaryWeapon;
        public Weapon GetSecondaryWeapon() => secondaryWeapon;
        public RangedWeapon GetCurrentRangedWeapon() => currentRangedWeapon;

        #endregion
    }
}
