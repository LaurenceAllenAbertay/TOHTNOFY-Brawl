using System;
using System.Collections;
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

        // ── Tile Effects ─────────────────────────────────────────────────────────
        private readonly List<TileEffectInstance> _activeEffects = new List<TileEffectInstance>();

        /// <summary>Read-only view of all effects currently active on this tile.</summary>
        public IReadOnlyList<TileEffectInstance> ActiveEffects => _activeEffects;

        /// <summary>True when at least one tile effect is currently active.</summary>
        public bool HasActiveEffects => _activeEffects.Count > 0;

        // Tracks the live persistent VFX GameObject for each active effect instance so it
        // can be destroyed when the effect expires or is dispelled.
        private readonly Dictionary<TileEffectInstance, GameObject> _persistentVFX
            = new Dictionary<TileEffectInstance, GameObject>();

        // ── Highlights ───────────────────────────────────────────────────────────
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

        // Clears transient highlights (movement range, ability preview) and returns to the
        // tile's base state. If the tile has active effects, the base state is Danger so the
        // hazard stays visually marked after ClearAllHighlights() runs.
        public void ResetHighlight()
        {
            ApplyHighlight(HasActiveEffects ? TileHighlightType.Danger : TileHighlightType.Normal);
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

        // ── Tile Effect Management ────────────────────────────────────────────────

        /// <summary>
        /// Adds an effect to this tile. If an identical effect (same effectData asset) is already
        /// present, refreshes duration instead of stacking a duplicate.
        /// </summary>
        public void AddEffect(TileEffectInstance effect)
        {
            if (effect == null || effect.effectData == null) return;

            foreach (var existing in _activeEffects)
            {
                if (existing.effectData == effect.effectData)
                {
                    existing.RefreshDuration(effect.remainingRounds);
                    existing.effectPower = Mathf.Max(existing.effectPower, effect.effectPower);
                    return;
                }
            }

            _activeEffects.Add(effect);

            // Spawn one-shot application VFX (impact flash, puff, etc.)
            effect.effectData.SpawnApplicationVFX(transform.position);

            // Spawn persistent VFX (fire, smoke, etc.) and track it for cleanup on expiry
            var persistent = effect.effectData.SpawnPersistentVFX(transform.position);
            if (persistent != null)
                _persistentVFX[effect] = persistent;

            // Immediately show hazard highlight so the tile is visually marked the moment
            // an effect lands, not only after the next ClearAllHighlights() call.
            Highlight(TileHighlightType.Danger);
        }

        /// <summary>
        /// Triggers all active effects as a coroutine (called by TurnManager at round end via
        /// TriggerEnvironmentEffects). Each effect Apply() can yield — e.g. to wait for a hurt
        /// animation before applying damage — so EndTurn waits for all feedback to finish before
        /// starting the next turn. Works identically for PlayerUnit and EnemyUnit on the tile.
        /// </summary>
        public IEnumerator TriggerEffects(int currentRound)
        {
            if (_activeEffects.Count == 0) yield break;

            var ctx = new TileEffectContext
            {
                tile         = this,
                currentRound = currentRound
            };

            foreach (var instance in _activeEffects)
            {
                if (instance.IsExpired) continue;

                // Re-read currentUnit each iteration. If the previous effect killed the unit,
                // currentUnit will be null here and Apply() will exit early rather than
                // firing ReceiveDamage on an already-dead unit.
                ctx.unitOnTile  = currentUnit;
                ctx.applier     = instance.applier;
                ctx.effectPower = instance.effectPower;

                // Apply() is a coroutine — yield on it so animations finish before we move on
                yield return instance.effectData.Apply(ctx);

                instance.remainingRounds--;
            }

            // Prune expired effects — spawn removal VFX and destroy persistent VFX for each
            var expired = _activeEffects.FindAll(e => e.IsExpired);
            foreach (var e in expired)
            {
                e.effectData.SpawnRemovalVFX(transform.position);
                DestroyPersistentVFX(e);
            }
            _activeEffects.RemoveAll(e => e.IsExpired);

            // Drop back to the normal visual once all effects have burned out
            if (_activeEffects.Count == 0)
                ResetHighlight();
        }

        /// <summary>
        /// Removes all active effects immediately (end of battle, dispel mechanic).
        /// </summary>
        public void ClearAllEffects()
        {
            foreach (var effect in _activeEffects)
            {
                effect.effectData.SpawnRemovalVFX(transform.position);
                DestroyPersistentVFX(effect);
            }
            _activeEffects.Clear();
            ResetHighlight();
        }

        // Destroys the persistent VFX instance for a given effect and removes it from tracking.
        private void DestroyPersistentVFX(TileEffectInstance effect)
        {
            if (_persistentVFX.TryGetValue(effect, out var vfxInstance))
            {
                if (vfxInstance != null)
                    Destroy(vfxInstance);
                _persistentVFX.Remove(effect);
            }
        }

        private void OnMouseDown()
        {
            OnTileClicked?.Invoke(this);
        }

    }
}