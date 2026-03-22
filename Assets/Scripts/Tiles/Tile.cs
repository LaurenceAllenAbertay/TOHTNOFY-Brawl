using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum TileHighlightType
    {
        Normal,
        Moveable,
        Occupied,
        Danger,
        AttackRange
    }

    public class Tile : MonoBehaviour
    {
        public static event Action<Tile> OnTileClicked;

        [Header("References")]
        [SerializeField] private Renderer tileRenderer;
        [SerializeField] private Color defaultColor = Color.white;

        [Header("Grid Info")]
        public Vector2Int gridPosition;

        [Tooltip("Whether this tile is passable terrain (walls, pits, etc. would be false)")]
        public bool passableTerrain = true;

        [Tooltip("Whether units can move onto this tile (considers both terrain and occupancy)")]
        public bool moveable => passableTerrain && !occupied;

        // Link to the unit currently on this tile
        public Unit currentUnit
        {
            get => _currentUnit;
            set
            {
                _currentUnit = value;
                occupied = (_currentUnit != null);
                // Note: We no longer modify 'moveable' here - it's calculated dynamically
            }
        }
        [SerializeField] private Unit _currentUnit;

        public bool occupied { get; private set; }

        // Current active highlight type
        private TileHighlightType currentHighlight = TileHighlightType.Normal;

        // Color mapping
        private static readonly Dictionary<TileHighlightType, Color> highlightColors = new Dictionary<TileHighlightType, Color>
        {
            { TileHighlightType.Normal, Color.white },
            { TileHighlightType.Moveable, new Color(0.6f, 1.0f, 1.0f) },
            { TileHighlightType.Occupied, Color.white * 0.6f },
            { TileHighlightType.Danger, new Color(1f, 0.5f, 0f) },
            { TileHighlightType.AttackRange, Color.red }
        };

        // Priority mapping (higher number = higher priority)
        private static readonly Dictionary<TileHighlightType, int> highlightPriority = new Dictionary<TileHighlightType, int>
        {
            { TileHighlightType.Normal, 0 },
            { TileHighlightType.Moveable, 1 },
            { TileHighlightType.Occupied, 2 },
            { TileHighlightType.Danger, 3 },
            { TileHighlightType.AttackRange, 4 }
        };

        private void Awake()
        {
            if (tileRenderer == null)
                tileRenderer = GetComponent<Renderer>();

            ApplyHighlight(TileHighlightType.Normal);
        }

        // Requests a highlight type. If it has higher priority than the current one, it replaces it.
        public void Highlight(TileHighlightType type)
        {
            if (type == TileHighlightType.Moveable && occupied)
            {
                type = TileHighlightType.Occupied;
            }

            if (highlightPriority[type] >= highlightPriority[currentHighlight])
            {
                ApplyHighlight(type);
            }
        }

        // Clears the highlight and returns to Normal.
        public void ResetHighlight()
        {
            ApplyHighlight(TileHighlightType.Normal);
        }

        // Applies a highlight immediately (bypassing priority checks).
        private void ApplyHighlight(TileHighlightType type)
        {
            currentHighlight = type;

            if (tileRenderer != null && highlightColors.TryGetValue(type, out Color color))
            {
                tileRenderer.material.color = color;
            }
        }

        private void OnMouseDown()
        {
            OnTileClicked?.Invoke(this);
        }

    }
}