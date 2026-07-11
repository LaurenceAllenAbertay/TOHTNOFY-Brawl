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
        
        public bool passableTerrain = true;

        public bool moveable => passableTerrain && !occupied;
        
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

        [SerializeField] private readonly List<TileEffectInstance> _activeEffects = new List<TileEffectInstance>();

        public IReadOnlyList<TileEffectInstance> ActiveEffects => _activeEffects;

        public bool HasActiveEffects => _activeEffects.Count > 0;
        
        private readonly Dictionary<TileEffectInstance, GameObject> _persistentVFX
            = new Dictionary<TileEffectInstance, GameObject>();
        
        private TileHighlightType currentHighlight = TileHighlightType.Normal;
        
        private static readonly Dictionary<TileHighlightType, Color> highlightColors = new Dictionary<TileHighlightType, Color>
        {
            { TileHighlightType.Normal, Color.white },
            { TileHighlightType.Moveable, new Color(0.6f, 1.0f, 1.0f) },
            { TileHighlightType.Occupied, Color.white * 0.6f },
            { TileHighlightType.Danger, new Color(1f, 0.5f, 0f) },
            { TileHighlightType.AttackRange, Color.red }
        };

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

        public void ResetHighlight()
        {
            if (HasActiveEffects)
                ApplyEffectColor();
            else
                ApplyHighlight(TileHighlightType.Normal);
        }

        private void ApplyHighlight(TileHighlightType type)
        {
            currentHighlight = type;

            if (tileRenderer != null && highlightColors.TryGetValue(type, out Color color))
            {
                tileRenderer.material.color = color;
            }
        }
        
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

            effect.effectData.SpawnApplicationVFX(transform.position);

            var persistent = effect.effectData.SpawnPersistentVFX(transform.position);
            if (persistent != null)
                _persistentVFX[effect] = persistent;

            ApplyEffectColor();
        }

        private void ApplyEffectColor()
        {
            if (_activeEffects.Count == 0) return;

            var effectColor = _activeEffects[_activeEffects.Count - 1].effectData.effectColor;
            currentHighlight = TileHighlightType.Danger; 
            if (tileRenderer != null)
                tileRenderer.material.color = effectColor;
        }
        
        public IEnumerator TriggerEffects(int currentRound)
        {
            if (_activeEffects.Count == 0) yield break;

            foreach (var instance in _activeEffects)
            {
                if (instance.IsExpired) continue;
                
                if (instance.effectData.triggerTiming == TriggerTiming.OnRoundEnd)
                {
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
            
            var expired = _activeEffects.FindAll(e => e.IsExpired);
            foreach (var e in expired)
            {
                e.effectData.SpawnRemovalVFX(transform.position);
                DestroyPersistentVFX(e);
            }
            _activeEffects.RemoveAll(e => e.IsExpired);

            if (_activeEffects.Count == 0)
                ResetHighlight();
        }

        public IEnumerator TriggerOnEnterEffects(Unit movingUnit)
        {
            if (_activeEffects.Count == 0) yield break;
            
            var onEnterEffects = _activeEffects
                .Where(e => !e.IsExpired && e.effectData.triggerTiming == TriggerTiming.OnEnter)
                .ToList();

            if (onEnterEffects.Count == 0) yield break;

            foreach (var instance in onEnterEffects)
            {
                var ctx = new TileEffectContext
                {
                    tile         = this,
                    unitOnTile   = movingUnit,
                    applier      = instance.applier,
                    effectPower  = instance.effectPower,
                    currentRound = 0 
                };

                yield return instance.effectData.Apply(ctx);
                
                if (movingUnit == null || movingUnit.currentHealth <= 0) yield break;
            }
        }

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

        private void DestroyPersistentVFX(TileEffectInstance effect)
        {
            if (_persistentVFX.TryGetValue(effect, out var vfxInstance))
            {
                if (vfxInstance != null)
                    Destroy(vfxInstance);
                _persistentVFX.Remove(effect);
            }
        }
        
        public static void NotifyClicked(Tile tile)     => OnTileClicked?.Invoke(tile);
        public static void NotifyHovered(Tile tile)     => OnTileHovered?.Invoke(tile);
        public static void NotifyHoverExited(Tile tile) => OnTileHoverExited?.Invoke(tile);

    }
}