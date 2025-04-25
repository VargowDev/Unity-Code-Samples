using System.Collections.Generic;
using UnityEngine;

namespace TheRevenantEngine.Equipment
{
    /// <summary>
    /// The InventoryGrid class manages a grid-based inventory system.
    /// It supports item placement, rotation, collision checks, and provides logic for querying valid grid positions.
    /// </summary>
    public class InventoryGrid : MonoBehaviour
    {
        [Header("Grid Size")]
        [SerializeField] private int gridWidth = 0;
        [SerializeField] private int gridHeight = 0;
        [Header("Cell Size")]
        [SerializeField] private float cellWidth = 100f;
        [SerializeField] private float cellHeight = 100f;
        [Space]
        [SerializeField] private GameObject gridFieldPrefab;

        private Transform inventoryGridContainer;
        private Inventory inventory;

        private Dictionary<Vector2Int, GridField> gridFields = new Dictionary<Vector2Int, GridField>();

        /// <summary>
        /// Initializes the grid layout and creates the field slots in the UI container.
        /// </summary>
        public void InitializeInventoryGrid(Transform inventoryGridContainer, Inventory inventory)
        {
            this.inventoryGridContainer = inventoryGridContainer;
            this.inventory = inventory;

            foreach (var kvp in gridFields)
            {
                Destroy(kvp.Value.gameObject);
            }
            gridFields.Clear();

            float totalGridWidth = gridWidth * cellWidth;
            float totalGridHeight = gridHeight * cellHeight;

            float startX = -totalGridWidth / 2f;
            float startY = totalGridHeight / 2f;

            for (int i = 0; i < gridHeight; i++)
            {
                for (int j = 0; j < gridWidth; j++)
                {
                    GameObject newGridFieldGO = Instantiate(gridFieldPrefab, inventoryGridContainer);
                    newGridFieldGO.transform.localPosition = new Vector3(startX + j * cellWidth, startY - i * cellHeight, 0);
                    GridField field = newGridFieldGO.GetComponent<GridField>();
                    field.Setup(j, i);
                    Vector2Int key = new Vector2Int(j, i);
                    gridFields.Add(key, field);
                }
            }
        }

        /// <summary>
        /// Returns the grid field at the specified coordinates, or null if not found.
        /// </summary>
        public GridField GetGridFieldAt(int x, int y)
        {
            Vector2Int key = new Vector2Int(x, y);
            gridFields.TryGetValue(key, out GridField field);
            return field;
        }

        /// <summary>
        /// Finds the nearest available slot for an item based on the pointer position.
        /// </summary>
        public Vector2Int? GetNearestAvailableSlotForItem(Vector2 pointerScreenPos, int itemWidth, int itemHeight)
        {
            Vector2Int? bestSlot = null;
            float minDistance = float.MaxValue;
            Canvas canvas = GetComponentInParent<Canvas>();

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (!CanPlaceItem(x, y, itemWidth, itemHeight))
                        continue;

                    GridField field = GetGridFieldAt(x, y);
                    Vector2 fieldScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, field.transform.position);

                    float distance = Vector2.Distance(pointerScreenPos, fieldScreenPos);
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        bestSlot = new Vector2Int(x, y);
                    }
                }
            }

            return bestSlot;
        }

        /// <summary>
        /// Checks if the item of given size can be placed starting at (startX, startY).
        /// </summary>
        public bool CanPlaceItem(int startX, int startY, int itemWidth, int itemHeight)
        {
            if (startX < 0 || startY < 0 ||
                startX + itemWidth > gridWidth ||
                startY + itemHeight > gridHeight)
            {
                return false;
            }

            for (int y = startY; y < startY + itemHeight; y++)
            {
                for (int x = startX; x < startX + itemWidth; x++)
                {
                    GridField field = GetGridFieldAt(x, y);
                    if (field == null || field.IsOccupied)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Marks cells as occupied for an item starting at (startX, startY)
        /// with size (itemWidth x itemHeight).
        /// </summary>
        public void MarkSlotsOccupation(bool isOccupied, int startX, int startY, int itemWidth, int itemHeight, ItemVisual itemVisual)
        {
            for (int y = startY; y < startY + itemHeight; y++)
            {
                for (int x = startX; x < startX + itemWidth; x++)
                {
                    GridField field = GetGridFieldAt(x, y);
                    if (field != null)
                    {
                        field.UpdateState(isOccupied, itemVisual);
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether the item can be placed while excluding a defined area (e.g., existing item's previous position).
        /// </summary>
        private bool CanPlaceItemAtPosExcludingArea(int startX, int startY, int itemWidth, int itemHeight, int excludeX, int excludeY, int excludeW, int excludeH)
        {
            // Basic boundary checks for the item itself
            if (startX < 0 || startY < 0 || startX + itemWidth > gridWidth || startY + itemHeight > gridHeight)
            {
                return false;
            }

            // Check each cell the item would occupy
            for (int y = startY; y < startY + itemHeight; y++)
            {
                for (int x = startX; x < startX + itemWidth; x++)
                {
                    // Is this cell within the excluded area?
                    bool isExcluded = (x >= excludeX && x < excludeX + excludeW && y >= excludeY && y < excludeY + excludeH);
                    if (isExcluded)
                    {
                        return false; // Cannot place item overlapping the excluded (e.g., weapon's future) area
                    }

                    // Is this cell already occupied by something else?
                    GridField field = GetGridFieldAt(x, y);
                    if (field == null || field.IsOccupied) // Check IsOccupied AFTER exclusion check
                    {
                        return false; // Cell is invalid or occupied by another existing item
                    }
                }
            }

            // All cells are valid, not excluded, and not occupied
            return true;
        }

        /// <summary>
        /// Finds the first available slot that excludes a specific area, with rotation fallback.
        /// </summary>
        public Vector2Int? GetFirstAvailableSlotForItemExcludingArea(int itemWidth, int itemHeight, int excludeX, int excludeY, int excludeW, int excludeH, out bool wasRotated)
        {
            wasRotated = false;

            // Try original orientation
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (CanPlaceItemAtPosExcludingArea(x, y, itemWidth, itemHeight, excludeX, excludeY, excludeW, excludeH))
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }

            // Try rotated orientation (if item can rotate)
            if (itemWidth != itemHeight) // Only try rotation if dimensions are different
            {
                for (int y = 0; y < gridHeight; y++)
                {
                    for (int x = 0; x < gridWidth; x++)
                    {
                        // Swap itemWidth and itemHeight for rotated check
                        if (CanPlaceItemAtPosExcludingArea(x, y, itemHeight, itemWidth, excludeX, excludeY, excludeW, excludeH))
                        {
                            wasRotated = true;
                            return new Vector2Int(x, y);
                        }
                    }
                }
            }


            // No slot found in either orientation
            return null;
        }

        /// <summary>
        /// Resets the state of all fields in the grid – sets them to free.
        /// </summary>
        public void ResetOccupiedSlots()
        {
            foreach (var kvp in gridFields)
            {
                GridField field = kvp.Value;
                field.UpdateState(false, null);
            }
        }

        /// <summary>
        /// Places an item visually and marks corresponding grid slots as occupied.
        /// </summary>
        public void PlaceItem(ItemVisual itemVisual, int startX, int startY)
        {
            int itemWidth = itemVisual.itemEntry.GetCurrentGridWidth();
            int itemHeight = itemVisual.itemEntry.GetCurrentGridHeight();

            MarkSlotsOccupation(true, startX, startY, itemWidth, itemHeight, itemVisual);
            inventory.PlaceItem(itemVisual.itemEntry, startX, startY);

            itemVisual.itemEntry.gridX = startX;
            itemVisual.itemEntry.gridY = startY;
            
            GridField topLeftField = GetGridFieldAt(startX, startY);
            itemVisual.transform.SetParent(inventoryGridContainer, false);
            itemVisual.transform.position = topLeftField.transform.position;
        }

        /// <summary>
        /// Removes an item from the grid and frees its occupied slots.
        /// </summary>
        public void RemoveItem(ItemVisual itemVisual)
        {
            int startX = itemVisual.itemEntry.gridX;
            int startY = itemVisual.itemEntry.gridY;
            int width = itemVisual.itemEntry.GetCurrentGridWidth();
            int height = itemVisual.itemEntry.GetCurrentGridHeight();

            for (int y = startY; y < startY + height; y++)
            {
                for (int x = startX; x < startX + width; x++)
                {
                    GridField field = GetGridFieldAt(x, y);
                    if (field != null && field.GetItemVisual() == itemVisual)
                    {
                        field.UpdateState(false, null);
                    }
                }
            }
        }

        /// <summary>
        /// Searches for the first available slot for the given item dimensions. Includes rotation fallback.
        /// </summary>
        public Vector2Int? GetFirstAvailableSlotForItem(int itemWidth, int itemHeight, out bool wasRotated)
        {
            wasRotated = false;

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (CanPlaceItem(x, y, itemWidth, itemHeight))
                    {
                        return new Vector2Int(x, y);
                    }
                }
            }

            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    if (CanPlaceItem(x, y, itemHeight, itemWidth))
                    {
                        wasRotated = true;
                        return new Vector2Int(x, y);
                    }
                }
            }

            return null;
        }

        /// <summary>Returns the width of a single grid cell.</summary>
        public float GetCellWidth() => cellWidth;

        /// <summary>Returns the height of a single grid cell.</summary>
        public float GetCellHeight() => cellHeight;

        /// <summary>Returns the Inventory reference assigned to this grid.</summary>
        public Inventory GetInventory() => inventory;
    }

}
