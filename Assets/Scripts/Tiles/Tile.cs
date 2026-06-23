using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
        public static event Action<Tile> OnTileHovered;
        public static event Action<Tile> OnTileHoverExited;

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
            }
        }
        [SerializeField] private Unit _currentUnit;

        public bool occupied { get; private set; }

        /// <summary>
        /// Emergency fallback: instantly moves <paramref name="unit"/> to the nearest
        /// passable, unoccupied tile (BFS outward from <paramref name="origin"/>).
        /// Only called from Unit.SetCurrentTile when a unit tries to come to rest on an
        /// already-occupied tile — never during animated movement.
        /// </summary>
        internal static void TeleportToClosestFreeTile(Unit unit, Tile origin)
        {
            if (unit == null || origin == null) return;

            var visited = new HashSet<Tile> { origin };
            var queue   = new Queue<Tile>();

            if (GridManager.Instance != null)
            {
                foreach (var adj in GridManager.Instance.GetAdjacentTiles(origin))
                {
                    if (adj != null && !visited.Contains(adj))
                    {
                        visited.Add(adj);
                        queue.Enqueue(adj);
                    }
                }
            }

            while (queue.Count > 0)
            {
                Tile candidate = queue.Dequeue();

                if (candidate.passableTerrain && !candidate.occupied)
                {
                    Debug.LogWarning(
                        $"[Tile] Teleporting '{unit.name}' from '{origin.name}' " +
                        $"to '{candidate.name}' (double-occupancy recovery).");
                    unit.SetCurrentTile(candidate);
                    return;
                }

                if (GridManager.Instance != null)
                {
                    foreach (var adj in GridManager.Instance.GetAdjacentTiles(candidate))
                    {
                        if (adj != null && !visited.Contains(adj))
                        {
                            visited.Add(adj);
                            queue.Enqueue(adj);
                        }
                    }
                }
            }

            Debug.LogError(
                $"[Tile] Could not find any free tile to teleport '{unit.name}' to — " +
                $"unit remains on '{origin.name}'. The map may be completely blocked.");
        }

        // ── Tile Effects ─────────────────────────────────────────────────────────
        [SerializeField] private readonly List<TileEffectInstance> _activeEffects = new List<TileEffectInstance>();

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
            if (HasActiveEffects)
                ApplyEffectColor();
            else
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
                    // VFX is intentionally not re-spawned on refresh. Re-applying fire to a tile
                    // that is already on fire shouldn't flash again. If a future effect needs a
                    // visual "reapplication" (e.g. ice refreezing), override this behaviour in a
                    // subclass by calling SpawnApplicationVFX() from within Apply() at the right moment.
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

            ApplyEffectColor();
        }
        
        // Applies the colour from the highest-priority active effect directly to the renderer,
        // bypassing the enum highlight system which has no concept of custom per-effect colours.
        // Uses the last-added effect's colour since that is the most recently placed hazard.
        private void ApplyEffectColor()
        {
            if (_activeEffects.Count == 0) return;

            // Use the most recently added effect's colour
            var effectColor = _activeEffects[_activeEffects.Count - 1].effectData.effectColor;
            currentHighlight = TileHighlightType.Danger; // Keep logical state as Danger for priority checks
            if (tileRenderer != null)
                tileRenderer.material.color = effectColor;
        }

        /// <summary>
        /// Fires at round end via TurnManager.TriggerEnvironmentEffects.
        /// OnRoundEnd effects execute their Apply() coroutine.
        /// OnEnter effects only tick their duration — they fire via TriggerOnEnterEffects instead.
        /// </summary>
        public IEnumerator TriggerEffects(int currentRound)
        {
            if (_activeEffects.Count == 0) yield break;

            foreach (var instance in _activeEffects)
            {
                if (instance.IsExpired) continue;

                // Always tick the duration so OnEnter effects expire on the same schedule.
                // Only Apply OnRoundEnd effects here.
                if (instance.effectData.triggerTiming == TriggerTiming.OnRoundEnd)
                {
                    // Fresh context per iteration — see comments in TriggerOnEnterEffects.
                    var ctx = new TileEffectContext
                    {
                        tile         = this,
                        unitOnTile   = currentUnit,
                        applier      = instance.applier,
                        effectPower  = instance.effectPower,
                        currentRound = currentRound
                    };

                    yield return instance.effectData.Apply(ctx);
                }

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
        /// Fires when a unit moves onto or through this tile during movement.
        /// Triggers all OnEnter effects for the moving unit, regardless of whether
        /// they are the tile's currentUnit (they may still be mid-path).
        /// Does NOT decrement duration — that is handled at round end by TriggerEffects
        /// so an effect can fire multiple times per round without expiring early.
        /// Safe for both PlayerUnit and EnemyUnit.
        /// </summary>
        public IEnumerator TriggerOnEnterEffects(Unit movingUnit)
        {
            if (_activeEffects.Count == 0) yield break;

            // Snapshot OnEnter effects — iterate a copy so the main list can be safely
            // modified by Apply() if it kills the unit and prunes effects.
            var onEnterEffects = _activeEffects
                .Where(e => !e.IsExpired && e.effectData.triggerTiming == TriggerTiming.OnEnter)
                .ToList();

            if (onEnterEffects.Count == 0) yield break;

            foreach (var instance in onEnterEffects)
            {
                // A fresh context per iteration prevents a resuming coroutine from reading
                // stale data written by the next iteration (same pattern as TriggerEffects).
                var ctx = new TileEffectContext
                {
                    tile         = this,
                    unitOnTile   = movingUnit,   // The unit in motion, not currentUnit
                    applier      = instance.applier,
                    effectPower  = instance.effectPower,
                    currentRound = 0             // Not a round-end trigger
                };

                yield return instance.effectData.Apply(ctx);

                // If the effect killed the unit, stop processing further effects on it.
                if (movingUnit == null || movingUnit.currentHealth <= 0) yield break;
            }
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

        // ── Input event dispatchers ───────────────────────────────────────────────
        // Called exclusively by InputManager so tile interaction events are driven by
        // a tile-layer-only raycast, bypassing wall/occluder colliders on other layers.

        public static void NotifyClicked(Tile tile)     => OnTileClicked?.Invoke(tile);
        public static void NotifyHovered(Tile tile)     => OnTileHovered?.Invoke(tile);
        public static void NotifyHoverExited(Tile tile) => OnTileHoverExited?.Invoke(tile);

    }
}