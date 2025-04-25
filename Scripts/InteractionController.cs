using StarterAssets;
using TheRevenantEngine.Enemies;
using TheRevenantEngine.UI;
using UnityEngine;

namespace TheRevenantEngine.Player
{
    /// <summary>
    /// Handles interaction between the player and the world using raycasts.
    /// Detects interactable objects and enemies under the crosshair and shows appropriate UI.
    /// </summary>
    public class InteractionController : MonoBehaviour
    {
        [SerializeField] private float rayDistance = 50f;
        [SerializeField] private float interactionDistance = 1.0f;
        [SerializeField] private LayerMask interactableLayer;

        private IInteractable currentInteractable;
        private Enemy currentEnemy;

        private StarterAssetsInputs starterAssetsInputs;
        private Camera mainCamera;

        private void Awake()
        {
            starterAssetsInputs = GetComponent<StarterAssetsInputs>();
            mainCamera = Camera.main;
        }
        private void Update()
        {
            if (mainCamera == null) 
                return;
            HandleInteractionRaycast();
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Casts a ray from the center of the screen and handles interaction or enemy detection.
        /// </summary>
        private void HandleInteractionRaycast()
        {
            if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen())
            {
                HideEnemyUI();
                ClearInteractable();
                CrosshairUI.Instance.SetCrosshairColor(CrosshairState.Default);
                return;
            }

            Ray ray = mainCamera.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0));
            if (Physics.Raycast(ray, out RaycastHit hit, rayDistance, interactableLayer))
            {
                float dist = Vector3.Distance(transform.position, hit.point);

                Enemy enemy = hit.collider.GetComponentInParent<Enemy>();
                if (enemy != null)
                {
                    ShowEnemyUI(enemy);
                    CrosshairUI.Instance.SetCrosshairColor(CrosshairState.Enemy);
                    return;
                }
                else
                {
                    HideEnemyUI();
                }

                IInteractable interactable = hit.collider.GetComponent<IInteractable>();
                if (dist <= interactionDistance && interactable != null)
                {
                    if (currentInteractable != interactable)
                    {
                        ClearInteractable();
                        currentInteractable = interactable;
                        (currentInteractable as MonoBehaviour)?.SendMessage("Highlight", true, SendMessageOptions.DontRequireReceiver);
                    }

                    CrosshairUI.Instance.SetCrosshairColor(CrosshairState.Interactable);
                    InteractionTooltipUI.Instance.ShowMessage(currentInteractable.GetInteractionMessage(), currentInteractable.GetInteractableName());

                    if (starterAssetsInputs.interact)
                    {
                        currentInteractable.Interact();
                        currentInteractable.PlayInteractionSound();
                        starterAssetsInputs.InteractInput(false);
                    }
                    return;
                }
                else
                {
                    ClearInteractable();
                }
            }

            starterAssetsInputs.InteractInput(false);
            CrosshairUI.Instance.SetCrosshairColor(CrosshairState.Default);
            HideEnemyUI();
            ClearInteractable();
        }

        /// <summary>
        /// Clears the current interactable reference and removes highlight/tooltip UI.
        /// </summary>
        private void ClearInteractable()
        {
            if (currentInteractable != null)
            {
                MonoBehaviour mb = currentInteractable as MonoBehaviour;
                if (mb != null)
                {
                    mb.SendMessage("Highlight", false, SendMessageOptions.DontRequireReceiver);
                }
                currentInteractable = null;
            }

            InteractionTooltipUI.Instance.HideMessage();
        }

        /// <summary>
        /// Displays enemy information UI for the targeted enemy.
        /// </summary>
        /// <param name="enemy">The enemy to display info for.</param>
        private void ShowEnemyUI(Enemy enemy)
        {
            if (currentEnemy != enemy)
            {
                currentEnemy = enemy;
            }

            TargetHandlerUI.Instance.ShowEnemyInfo(enemy);
        }

        /// <summary>
        /// Hides enemy information UI.
        /// </summary>
        private void HideEnemyUI()
        {
            if (currentEnemy != null)
            {
                currentEnemy = null;
            }
            TargetHandlerUI.Instance.HideEnemyInfo();
        }
    }
}

