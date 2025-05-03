using UnityEngine;
using GuildsAndGlory.MainSystems;
using UnityEngine.Splines;
using System.Collections.Generic;
using System;
using GuildsAndGlory.Buildings;
using Unity.AI.Navigation;
using System.Collections;
using GuildsAndGlory.Economy;
using GuildsAndGlory.UI;
using UnityEngine.EventSystems;



namespace GuildsAndGlory.BuildingSystems
{
    /// <summary>
    /// Central manager for building placement operations. Handles both regular and spline-based building placement,
    /// ghost visualization, placement validation, and navigation mesh updates. Manages construction lifecycle from
    /// placement preview to final instantiation.
    /// </summary>
    public class BuildingPlacementManager : MonoBehaviour
    {
        private enum GhostMaterialState
        {
            White,
            Green,
            Red
        }

        public static BuildingPlacementManager Instance { get; private set; }

        public event EventHandler<BuildingBase> OnBuildingConstructionStart;

        [Header("Main Settings")]
        [SerializeField] private bool isBuildingModeActivated = false;
        [SerializeField] private NavMeshSurface terrainNavMeshSurface;
        [SerializeField] private Terrain mainTerrain;

        [Header("Snap Settings")]
        [SerializeField] private float snapThreshold = 1.0f;

        private BuildingTypeSO activeBuildingType;

        private ResourceGatherBase resourceGatherBase;
        private bool isResourceNodeNeeded = true;

        // Regular Buildings Parameters
        private GameObject buildingGhostVisual;
        private bool isGhostVisualCreated = false;
        private List<ResourceNode> resourceNodesInRange;

        // Spline Buildings Parameters
        private bool isBuildingSpline = false;
        private RoadSystem currentRoadSystem;
        private Vector3 lastSnappedPos;
        private bool wasSnappedLastFrame;

        // Resource Gathers Parameters
        private Collider gatherZoneCollider;
        private GatherZoneTriggerHandler gatherZoneTriggerHandler;
        private BuildingBase tempBuildingBase;

        [Header("Placement Settings")]
        [Tooltip("Minimum percentage of coverage (e.g. 0.98 = 98%).")]
        [Range(0.0f, 1.0f)]
        [SerializeField] private float coverageThreshold = 0.98f;
        [SerializeField] private float rotationSpeed = 90f;

        [Header("Ghost materials")]
        [SerializeField] private Material ghostMaterialWhite;
        [SerializeField] private Material ghostMaterialRed;
        [SerializeField] private Material ghostMaterialGreen;



        [Tooltip("The layer on which building is allowed.")]
        public LayerMask validPlacementLayer;

        private float currentRotationY = 0f;

        private GhostMaterialState currentGhostMaterialState = GhostMaterialState.White;

        private TerrainData clonedData;

        // Debug
        private List<PointSample> debugSamples = new List<PointSample>();
        private float debugCoverage = 0f;
        private bool debugIsValidPlacement = false;

        private void Awake()
        {
            if (Instance != null)
            {
                Debug.LogWarning("Multiple instances of BuildingPlacementManager detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (mainTerrain != null && mainTerrain.terrainData != null)
            {
                clonedData = Instantiate(mainTerrain.terrainData);
                mainTerrain.terrainData = clonedData;
            }
        }
        private void Start()
        {
            PlacementValidator.validPlacementLayer = validPlacementLayer;
        }
        private void Update()
        {
            if (EventSystem.current.IsPointerOverGameObject())
                return;

            if (Input.GetKeyDown(KeyCode.B))
            {
                if (isBuildingModeActivated)
                {
                    StartCoroutine(ExitBuildingMode());
                    return;
                }
                else
                {
                    EnterBuildingMode();
                }
            }

            if (activeBuildingType == null || !isBuildingModeActivated)
                return;

            // Handle regular or spline-based building mode
            if (!activeBuildingType.isSplineBuilding && !activeBuildingType.isResourceCollector)
            {
                HandleRegularBuildingPlacement();
            }
            else if (activeBuildingType.buildingCategory == BuildingCategory.ResourceCollector)
            {
                HandleResourceGatherPlacement();
            }
            else
            {
                HandleSplineBuildingPlacement();
            }



            // Building rotation

            if (Input.GetKey(KeyCode.Comma))
            {
                currentRotationY -= rotationSpeed * Time.deltaTime;
            }
            if (Input.GetKey(KeyCode.Period))
            {
                currentRotationY += rotationSpeed * Time.deltaTime;
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");

            if (scroll > 0)
            {
                currentRotationY += rotationSpeed * Time.deltaTime * 10f;
            }
            else if (scroll < 0)
            {
                currentRotationY -= rotationSpeed * Time.deltaTime * 10f;
            }
        }


        /////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        public void EnterBuildingMode()
        {
            isBuildingModeActivated = true;
            BuildingZonesManager.Instance.ShowAllBuildingZones();
            PlayerBuildingManager.Instance.TurnOnAllBuildingBoundary();
            ResourceNodesManager.Instance.TurnOnAllResourceNodeBoundary();
            BuildingPlacementUI.Instance.SetWindow();
        }

        /// <summary>
        /// Exits the building mode, canceling any ongoing construction.
        /// </summary>
        private IEnumerator ExitBuildingMode()
        {

            // Reset building mode state
            isBuildingModeActivated = false;
            isGhostVisualCreated = false;

            // Destroy ghost object if it exists
            DestroyObjectGhostVisual();

            activeBuildingType = null;

            BuildingZonesManager.Instance.HideAllBuildignZones();
            PlayerBuildingManager.Instance.TurnOffAllBuildingBoundary();
            ResourceNodesManager.Instance.TurnOffAllResourceNodeBoundary();

            // If a spline is being built, cancel it
            if (isBuildingSpline)
            {
                CancelBuildingSplineObject();
            }

            BuildingCostInfo.Instance.HideWindow();
            BuildingPlacementUI.Instance.HideWindow();

            yield return null;
        }
        private void ClearTerrainDetailsUnderBuilding(Vector3 buildingWorldPos, Vector2 buildingSize, Quaternion buildingRot)
        {
            if (mainTerrain == null)
            {
                Debug.LogWarning("No reference to mainTerrain in BuildingPlacementManager!");
                return;
            }

            TerrainData terrainData = mainTerrain.terrainData;

            int detailWidth = terrainData.detailWidth;
            int detailHeight = terrainData.detailHeight;

            Vector3 terrainPos = mainTerrain.transform.position;

            float relativeX = (buildingWorldPos.x - terrainPos.x) / terrainData.size.x;
            float relativeZ = (buildingWorldPos.z - terrainPos.z) / terrainData.size.z;

            int detailX = Mathf.FloorToInt(relativeX * detailWidth);
            int detailZ = Mathf.FloorToInt(relativeZ * detailHeight);

            int clearRangeX = Mathf.CeilToInt((buildingSize.x / terrainData.size.x) * detailWidth);
            int clearRangeZ = Mathf.CeilToInt((buildingSize.y / terrainData.size.z) * detailHeight);

            int startX = detailX - clearRangeX / 2;
            int startZ = detailZ - clearRangeZ / 2;
            int endX = startX + clearRangeX;
            int endZ = startZ + clearRangeZ;

            startX = Mathf.Clamp(startX, 0, detailWidth);
            startZ = Mathf.Clamp(startZ, 0, detailHeight);
            endX = Mathf.Clamp(endX, 0, detailWidth);
            endZ = Mathf.Clamp(endZ, 0, detailHeight);

            int layerCount = terrainData.detailPrototypes.Length;
            for (int layer = 0; layer < layerCount; layer++)
            {
                int areaWidth = endX - startX;
                int areaHeight = endZ - startZ;
                int[,] details = terrainData.GetDetailLayer(startX, startZ, areaWidth, areaHeight, layer);

                for (int localX = 0; localX < areaWidth; localX++)
                {
                    for (int localZ = 0; localZ < areaHeight; localZ++)
                    {
                        int globalX = startX + localX;
                        int globalZ = startZ + localZ;

                        float normX = (float)globalX / detailWidth;
                        float normZ = (float)globalZ / detailHeight;
                        float worldX = terrainPos.x + normX * terrainData.size.x;
                        float worldZ = terrainPos.z + normZ * terrainData.size.z;

                        Vector3 worldPos = new Vector3(worldX, buildingWorldPos.y, worldZ);

                        if (IsInsideRotatedFootprint(worldPos, buildingWorldPos, buildingSize, buildingRot))
                        {
                            details[localZ, localX] = 0;
                        }
                    }
                }

                terrainData.SetDetailLayer(startX, startZ, layer, details);
            }
        }

        /// <summary>
        /// Checks if point worldPos lies within the rectangular base of the building,
        /// whose center is buildingCenter, size buildingSize, and rotation buildingRot.
        /// We assume that buildingSize.x = width, buildingSize.y = depth.
        /// </summary>
        private bool IsInsideRotatedFootprint(Vector3 worldPos, Vector3 buildingCenter, Vector2 buildingSize, Quaternion buildingRot)
        {
            Vector3 dir = worldPos - buildingCenter;

            Quaternion invRot = Quaternion.Inverse(buildingRot);
            Vector3 localPos = invRot * dir;

            float halfW = buildingSize.x * 0.5f;
            float halfD = buildingSize.y * 0.5f;

            if (Mathf.Abs(localPos.x) <= halfW && Mathf.Abs(localPos.z) <= halfD)
            {
                return true;
            }
            return false;
        }

        #region Regular Building Mode

        /// <summary>
        /// Handles placement of regular (non-spline) buildings.
        /// </summary>
        private void HandleRegularBuildingPlacement()
        {
            if (!isGhostVisualCreated)
            {
                CreateObjectGhostVisual();
                isGhostVisualCreated = true;
            }

            UpdateObjectGhostVisualPosition();

            debugIsValidPlacement = IsGhostPlacementValid(out debugCoverage, out debugSamples);

            if (activeBuildingType.isResourceGatherBuilding)
            {
                FindResourceNodesInRange();
            }


            if (InputManager.Instance.IsLMBDown())
            {
                if (debugIsValidPlacement)
                {
                    PlaceBuilding(buildingGhostVisual.transform.position);
                }
                else
                {
                    Debug.Log("Building cannot be erected - insufficient coverage or collision!");
                }
            }
        }

        /// <summary>
        /// Places a building at the current mouse position and destroys the ghost object.
        /// </summary>
        private void PlaceBuilding(Vector3 placePos)
        {
            Quaternion rotation = Quaternion.Euler(0f, currentRotationY, 0f);
            var placedBuilding = Instantiate(activeBuildingType.buildingPrefab, placePos, rotation);
            placedBuilding.layer = LayerMask.NameToLayer("Buildings");

            BuildingBase placedBuildingBase = placedBuilding.GetComponent<BuildingBase>();
            if(placedBuildingBase == null)
                placedBuildingBase = placedBuilding.AddComponent<BuildingBase>();

            placedBuildingBase.SetNavMeshVolumesEnabled(true);

            placedBuildingBase.InitializeBuilding(activeBuildingType);

            OnBuildingConstructionStart?.Invoke(this, placedBuildingBase);

            ClearTerrainDetailsUnderBuilding(placePos, placedBuildingBase.GetBuildingTypeSO().buildingSize, rotation);

            DestroyObjectGhostVisual();
            StartCoroutine(ExitBuildingMode());
        }

        /// <summary>
        /// Updates navigation mesh in specific bounds after building placement
        /// </summary>
        public void UpdateNavMeshForBuilding(List<NavMeshModifierVolume> modifierVolumes)
        {
            if (terrainNavMeshSurface == null)
            {
                Debug.LogError("NavMeshSurface not found!");
                return;
            }

            Bounds totalBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool hasBounds = false;

            foreach (var volume in modifierVolumes)
            {
                if (volume == null) continue;

                Bounds volumeBounds = new Bounds(volume.transform.position, volume.size);

                if (!hasBounds)
                {
                    totalBounds = volumeBounds;
                    hasBounds = true;
                }
                else
                {
                    totalBounds.Encapsulate(volumeBounds);
                }
            }

            if (!hasBounds)
            {
                Debug.LogWarning("No valid bounds found for NavMesh update.");
                return;
            }

            StartCoroutine(UpdateNavMeshPartial(totalBounds));
        }
        private IEnumerator UpdateNavMeshPartial(Bounds bounds)
        {
            yield return null;

            //Debug.Log($"Updating NavMesh for area: {bounds.center} with size {bounds.size}");
            terrainNavMeshSurface.UpdateNavMesh(terrainNavMeshSurface.navMeshData);
        }

        public void FindResourceNodesInRange()
        {
            resourceNodesInRange = gatherZoneTriggerHandler.GetNodesCenterInsideRange();
        }

        #endregion

        #region Resource Collectors BuildingMode

        /// <summary>
        /// Handles the placement of resource gatherer buildings. Updates the ghost visual position and checks for valid resource nodes.
        /// Changes the ghost material to red if no valid node is found, or green if a valid node is hovered.
        /// Places the resource gatherer building if a valid node is clicked.
        /// </summary
        private void HandleResourceGatherPlacement()
        {
            if (!isGhostVisualCreated || buildingGhostVisual == null)
            {
                isGhostVisualCreated = true;
            }

            UpdateObjectGhostVisualPosition();

            ChangeGhostsVisualMaterials(GhostMaterialState.Red);

            if (isResourceNodeNeeded)
            {
                ResourceNode hoveredNode = GetHoveredResourceNode();
                ResourceNodeSO resourceNodeSO = null;

                if (hoveredNode != null)
                {
                    resourceNodeSO = hoveredNode.GetResourceNodeSO();
                }

                if (hoveredNode != null && !hoveredNode.IsAlreadyGatherPlaced() && resourceNodeSO.resourceSO == tempBuildingBase.GetBuildingTypeSO().collectedRawMaterial)
                {
                    buildingGhostVisual.transform.position = hoveredNode.GetSnapingPoint().position;
                    buildingGhostVisual.transform.rotation = hoveredNode.GetSnapingPoint().rotation;
                    ChangeGhostsVisualMaterials(GhostMaterialState.Green);
                }

                if (InputManager.Instance.IsLMBDown())
                {
                    if (hoveredNode != null && !hoveredNode.IsAlreadyGatherPlaced() && resourceNodeSO.resourceSO == tempBuildingBase.GetBuildingTypeSO().collectedRawMaterial)
                    {
                        PlaceResourceGatherBuilding(hoveredNode);
                    }
                    else
                    {
                        Debug.Log("No valid ResourceNode found!");
                    }
                }
            }
            else
            {
                bool coverageOK = IsGhostPlacementValid(out _, out _);
                if (coverageOK && IsGhostInsideGatherZone())
                {
                    ChangeGhostsVisualMaterials(GhostMaterialState.Green);
                    if (InputManager.Instance.IsLMBDown())
                    {
                        PlaceResourceGatherBuilding(null);
                    }
                }
                else
                {
                    ChangeGhostsVisualMaterials(GhostMaterialState.Red);
                }
            }
        }

        /// <summary>
        /// Initiates the placement process for a resource gatherer building. Sets up the ghost visual and activates building mode.
        /// </summary>
        /// <param name="buildingBase">The building base component of the resource gatherer.</param>
        public void StartResourceGatherPlacement(BuildingBase buildingBase)
        {
            if (isBuildingModeActivated) return;

            tempBuildingBase = buildingBase;
            buildingBase.GetBuildingGizmoDrawer().ShowGatheringZone();

            resourceGatherBase = buildingBase.GetBuildingTypeSO().collectorPrefab.GetComponent<ResourceGatherBase>();
            activeBuildingType = resourceGatherBase.GetBuildingType();
            gatherZoneTriggerHandler = buildingBase.GetGatherZoneTriggerHandler();
            gatherZoneCollider = buildingBase.GetBuildingGizmoDrawer().GetGatheringZoneCollider();

            isResourceNodeNeeded = resourceGatherBase.IsResourceNodeNeeded();
            isBuildingModeActivated = true;
            isGhostVisualCreated = false;

            if(!isResourceNodeNeeded)
            {
                BuildingZonesManager.Instance.ShowAllBuildingZones();
                PlayerBuildingManager.Instance.TurnOnAllBuildingBoundary();
                ResourceNodesManager.Instance.TurnOnAllResourceNodeBoundary();
            }

            buildingGhostVisual = Instantiate(buildingBase.GetBuildingTypeSO().collectorPrefab, Vector3.zero, Quaternion.identity);
            ChangeGhostsVisualMaterials(GhostMaterialState.Red);
        }

        /// <summary>
        /// Places the resource gatherer building at the specified resource node. Initializes the gatherer and links it to the node.
        /// </summary>
        /// <param name="node">The resource node where the gatherer will be placed.</param>
        private void PlaceResourceGatherBuilding(ResourceNode node)
        {
            var originalPrefab = tempBuildingBase.GetBuildingTypeSO().collectorPrefab;
            Vector3 position = buildingGhostVisual.transform.position;
            Quaternion rotation = buildingGhostVisual.transform.rotation;

            GameObject placedGather = Instantiate(originalPrefab, position, rotation);

            ResourceGatherBase resourceGather = placedGather.GetComponent<ResourceGatherBase>();
            if (resourceGather != null)
            {
                if(node != null)
                {
                    resourceGather.InitializeResourceGather(node);
                    node.SetIsAlreadyGatherPlaced(true);

                    Transform gatherProdPoint = resourceGather.GetGatherProductionPoint();
                    tempBuildingBase.AddProductionPoint(gatherProdPoint);
                    tempBuildingBase.GetBuildingWareFactory().IncreaseStandingGathersAmount();
                }
                else
                {
                    tempBuildingBase.GetBuildingWareFactory().IncreaseStandingGathersAmount();
                    resourceGather.InitializeResourceGather();
                    placedGather.layer = LayerMask.NameToLayer("Buildings");
                }
            }

            DestroyObjectGhostVisual();
            tempBuildingBase.GetBuildingGizmoDrawer().HideGatheringZone();
            tempBuildingBase = null;
            StartCoroutine(ExitBuildingMode());
        }

        /// <summary>
        /// Retrieves the resource node currently hovered by the mouse cursor.
        /// </summary>
        /// <returns>The hovered resource node, or null if no valid node is found.</returns>
        private ResourceNode GetHoveredResourceNode()
        {
            Ray ray = Camera.main.ScreenPointToRay(InputManager.Instance.GetMouseScreenPosition());
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, LayerMask.GetMask("ResourceNodes")))
            {
                return hit.collider.GetComponent<ResourceNode>();
            }

            return null;
        }

        private bool IsGhostInsideGatherZone()
        {
            if (gatherZoneTriggerHandler == null) return false;

            float distance = Vector3.Distance(gatherZoneTriggerHandler.transform.position, buildingGhostVisual.transform.position);
            float maxRadius = gatherZoneTriggerHandler.GetRadius();
            return distance <= maxRadius;
        }

        #endregion

        #region Spline Building Mode

        /// <summary>
        /// Handles the placement of roads using splines.
        /// </summary>
        private void HandleSplineBuildingPlacement()
        {

            if (buildingGhostVisual != null)
            {
                UpdateObjectGhostVisualPosition();
            }

            if (!isBuildingSpline)
            {
                if (!isGhostVisualCreated)
                {
                    CreateObjectGhostVisual();
                    isGhostVisualCreated = true;
                }
                if (Input.GetMouseButtonDown(0))
                {
                    StartBuildingSplineObject();
                }
                return;
            }
            if (currentRoadSystem != null)
            {
                UpdateCurrentSplinePoints();
            }

            if (Input.GetMouseButtonDown(0))
                AddSplinePointAtMouse();

            if (Input.GetKeyDown(KeyCode.Return))
                FinishBuildingSplineObject();

            if (Input.GetMouseButtonDown(1))
                CancelBuildingSplineObject();

        }

        /// <summary>
        /// Starts the process of building a road using a spline.
        /// </summary>
        private void StartBuildingSplineObject()
        {
            Debug.Log("Start building a road");

            Vector3 rawPos = MouseWorld.GetWorldMousePosition();
            Vector3 snappedPos = RoadSystemManager.Instance.GetSnappedPosition(rawPos, snapThreshold, out bool isSnappedToExisting);

            currentRoadSystem = RoadSystemManager.Instance.CreateRoadSystem(activeBuildingType, snappedPos);

            isBuildingSpline = true;

            AddSplinePointAtMouse();
        }

        /// <summary>
        /// Updates the position of the last knot to follow the mouse (rubberband effect).
        /// </summary>
        private void UpdateCurrentSplinePoints()
        {
            if (currentRoadSystem == null) 
                return;

            Vector3 rawPos = MouseWorld.GetWorldMousePosition();
            Vector3 finalPos = RoadSystemManager.Instance.GetSnappedPosition(rawPos, snapThreshold, out bool isSnapped);

            // Snap position if close enough
            if (rawPos != lastSnappedPos || !wasSnappedLastFrame)
            {
                finalPos = RoadSystemManager.Instance.GetSnappedPosition(rawPos, snapThreshold, out bool isSnappedToExisting);
                lastSnappedPos = finalPos;
                wasSnappedLastFrame = isSnapped;
            }

            currentRoadSystem.UpdateLastPoint(finalPos);
        }

        /// <summary>
        /// Adds a new point at the end of the road spline.
        /// </summary>
        private void AddSplinePointAtMouse()
        {
            if (currentRoadSystem == null) 
                return;

            Vector3 rawPos = MouseWorld.GetWorldMousePosition();
            Vector3 finalPos = RoadSystemManager.Instance.GetSnappedPosition(rawPos, snapThreshold, out bool isSnappedToExisting);

            if (isSnappedToExisting && !currentRoadSystem.IsPositionAKnot(finalPos))
            {
                RoadSystem targetRoad = RoadSystemManager.Instance.GetRoadContainingPosition(finalPos);
                targetRoad?.SplitSplineAtPoint(finalPos);
            }

            currentRoadSystem.AddPoint(finalPos);
            Debug.Log("Added new road point");

        }

        /// <summary>
        /// Completes the road building process by finalizing the mesh.
        /// </summary>
        private void FinishBuildingSplineObject()
        {

            // Remove preview point
            if (currentRoadSystem != null)
            {
                currentRoadSystem.FinalizeRoad(removePreview: true);
            }

            isBuildingSpline = false;

            // Reset references
            ResetSplineReferences();

            DestroyObjectGhostVisual();
        }

        /// <summary>
        /// Cancels the road construction process and removes the current road object.
        /// </summary>
        private void CancelBuildingSplineObject()
        {
            if (currentRoadSystem != null)
            {
                currentRoadSystem.CancelRoad();
            }

            isBuildingSpline = false;

            ResetSplineReferences();
            DestroyObjectGhostVisual();
        }

        /// <summary>
        /// Resets all references related to the current spline and ghost object.
        /// </summary>
        private void ResetSplineReferences()
        {
            currentRoadSystem = null;
            isGhostVisualCreated = false;
        }

        #endregion

        #region Ghost Object Methods

        /// <summary>
        /// Updates the position of the ghost object to follow the mouse cursor.
        /// </summary>
        private void UpdateObjectGhostVisualPosition()
        {
            if (buildingGhostVisual == null)
                return;

            buildingGhostVisual.transform.position = MouseWorld.GetWorldMousePosition();
            Quaternion rotation = Quaternion.Euler(0, currentRotationY, 0);
            buildingGhostVisual.transform.rotation = rotation;
        }

        /// <summary>
        /// Creates the ghost object at the current mouse position.
        /// </summary>
        private void CreateObjectGhostVisual()
        {
            if (activeBuildingType == null)
                return;

            buildingGhostVisual = Instantiate(activeBuildingType.buildingPrefab, MouseWorld.GetWorldMousePosition(), Quaternion.identity);

            BuildingBase buildingBase = buildingGhostVisual.GetComponent<BuildingBase>();
            buildingBase.GetBuildingGizmoDrawer().ShowBoundary();

            if (buildingBase.GetBuildingTypeSO().isResourceGatherBuilding)
                buildingBase.GetBuildingGizmoDrawer().ShowGatheringZone();

            gatherZoneTriggerHandler = buildingBase.GetGatherZoneTriggerHandler();

            buildingBase.SwapMeshToFinishedBuilding();
            buildingBase.SetNavMeshVolumesEnabled(false);

            ChangeGhostsVisualMaterials(GhostMaterialState.White);

            isGhostVisualCreated = true;
        }

        /// <summary>
        /// Changes the material of the object depending on the validation of the construction.
        /// </summary>
        /// <param name="newState">Enum with assigned material</param>
        private void ChangeGhostsVisualMaterials(GhostMaterialState newState)
        {
            if (currentGhostMaterialState == newState)
                return;

            currentGhostMaterialState = newState;

            Material selectedMaterial = ghostMaterialWhite;
            switch (newState)
            {
                case GhostMaterialState.Green:
                    selectedMaterial = ghostMaterialGreen;
                    break;
                case GhostMaterialState.Red:
                    selectedMaterial = ghostMaterialRed;
                    break;
            }

            MeshRenderer[] meshRenderers = buildingGhostVisual.GetComponentsInChildren<MeshRenderer>();
            foreach (MeshRenderer meshRenderer in meshRenderers)
            {
                if (meshRenderer.gameObject.name == "BuildingBoundary" || meshRenderer.gameObject.name == "BuildingGatheringZone")
                {
                    continue;
                }


                Material[] newMaterials = new Material[meshRenderer.materials.Length];

                for (int i = 0; i < newMaterials.Length; i++)
                {
                    newMaterials[i] = selectedMaterial;
                }

                meshRenderer.materials = newMaterials;
            }
        }

        /// <summary>
        /// Destroys the ghost object.
        /// </summary>
        private void DestroyObjectGhostVisual()
        {

            if (buildingGhostVisual != null)
            {
                if (gatherZoneTriggerHandler != null)
                {
                    gatherZoneTriggerHandler.ResetAllNodesMaterials();
                }

                Destroy(buildingGhostVisual);
                isGhostVisualCreated = false;
            }
        }

        /// <summary>
        /// Checks if the gho-st (instantiated prefab) collides with the environment 
        /// (e.g. Buildings, Obstacle, Resources layers).
        /// </summary>
        /// <returns>True if ghost collide with something</returns>
        private bool IsGhostCollidingWithEnvironment()
        {
            if(buildingGhostVisual == null) 
                return false;

            BoxCollider bc = buildingGhostVisual.GetComponent<BoxCollider>();
            if (bc == null)
            {
                Debug.LogWarning($"Ghost prefab '{buildingGhostVisual.name}' has no BoxCollider!");
                return false;
            }

            Vector3 worldCenter = bc.transform.position + bc.transform.rotation * bc.center;
            Vector3 halfExtents = bc.size * .5f;

            LayerMask collisionMask = LayerMask.GetMask("Buildings", "Obstacles", "ResourceNodes");

            Collider[] hits = Physics.OverlapBox(worldCenter, halfExtents, bc.transform.rotation, collisionMask, QueryTriggerInteraction.Ignore);

            bool collisionDetected = (hits.Length > 0);
            return collisionDetected;
        }

        /// <summary>.
        /// Checks whether a building can be placed in the current position of the gho-sta
        /// (good coverage and no collision).
        /// </summary>.
        private bool IsGhostPlacementValid(out float coverage, out List<PointSample> samples)
        {
            coverage = 0f;
            samples = null;

            if (buildingGhostVisual == null)
                return false;

            Vector3 ghostPos = buildingGhostVisual.transform.position;
            Quaternion ghostRot = buildingGhostVisual.transform.rotation;

            bool coverageOK = PlacementValidator.CheckPlacement(activeBuildingType, ghostPos, ghostRot, coverageThreshold, out coverage, out samples);

            if (!coverageOK)
            {
                ChangeGhostsVisualMaterials(GhostMaterialState.Red);
                return false;
            }

            bool collisionDetected = IsGhostCollidingWithEnvironment();

            if (collisionDetected)
            {
                ChangeGhostsVisualMaterials(GhostMaterialState.Red);
                return false;
            }

            ChangeGhostsVisualMaterials(GhostMaterialState.Green);

            return true;
        }

        #endregion

        #region Debug Visualization

        /// <summary>
        /// We draw a grid of samples in the scene - green if valid, red if not.
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!isBuildingModeActivated)
                return;

            // We draw only in build mode
            Gizmos.color = Color.white;

            // We can preview coverage
            if (debugSamples != null)
            {
                foreach (var sample in debugSamples)
                {
                    Gizmos.color = sample.isValid ? Color.green : Color.red;
                    Gizmos.DrawSphere(sample.position, 0.1f);
                }
            }

        }

        #endregion

        #region Get/Set Methods

        public NavMeshSurface GetTerrainNavMeshSurface() { return terrainNavMeshSurface; }
        public bool IsBuildingModeActivated() { return isBuildingModeActivated; }
        public void ChangeActiveBuilding(BuildingTypeSO buildingType)
        {
            activeBuildingType = buildingType;
            isGhostVisualCreated = false;

            DestroyObjectGhostVisual();
            CreateObjectGhostVisual();
        }

        #endregion
    }
}

