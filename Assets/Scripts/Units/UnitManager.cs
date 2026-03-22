using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitManager : MonoBehaviour
    {
        public static UnitManager Instance { get; private set; }

        // Events for other systems to listen to
        public static event System.Action<Unit> OnUnitRegistered;
        public static event System.Action<Unit> OnUnitUnregistered;
        public static event System.Action<Unit> OnUnitMoved;
        public static event System.Action<Unit> OnUnitDied;

        // Unit collections for fast access
        private readonly List<Unit> allUnits = new List<Unit>();
        private readonly List<PlayerUnit> playerUnits = new List<PlayerUnit>();
        private readonly List<EnemyUnit> enemyUnits = new List<EnemyUnit>();

        // Cached collections to avoid allocations
        private readonly List<Unit> cachedValidTargets = new List<Unit>();
        private readonly List<Unit> cachedUnitsInRange = new List<Unit>();

        // Public read-only access
        public static IReadOnlyList<Unit> AllUnits => Instance?.allUnits ?? new List<Unit>();
        public static IReadOnlyList<PlayerUnit> PlayerUnits => Instance?.playerUnits ?? new List<PlayerUnit>();
        public static IReadOnlyList<EnemyUnit> EnemyUnits => Instance?.enemyUnits ?? new List<EnemyUnit>();

        private void Awake()
        {
            // Singleton setup
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Find all existing units in the scene and register them
            RegisterExistingUnits();

            //Debug.Log(UnitManager.AllUnits.Count);
        }

        private void RegisterExistingUnits()
        {
            // Only call FindObjectsOfType once during initialization
            var existingUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
            foreach (var unit in existingUnits)
            {
                // Check if unit is already in our list to avoid double registration
                if (!allUnits.Contains(unit))
                {
                    RegisterUnit(unit);
                }
            }
        }

        /// <summary>
        /// Register a unit with the manager. Call this from Unit.Awake() or when spawning units.
        /// </summary>
        public static void RegisterUnit(Unit unit)
        {
            if (Instance == null || unit == null) return;

            // Prevent double registration
            if (Instance.allUnits.Contains(unit)) return;

            Instance.allUnits.Add(unit);

            // Add to specific type lists
            if (unit is PlayerUnit player && !Instance.playerUnits.Contains(player))
                Instance.playerUnits.Add(player);
            else if (unit is EnemyUnit enemy && !Instance.enemyUnits.Contains(enemy))
                Instance.enemyUnits.Add(enemy);

            OnUnitRegistered?.Invoke(unit);
        }

        /// <summary>
        /// Unregister a unit from the manager. Call this from Unit.OnDestroy() or when units die.
        /// </summary>
        public static void UnregisterUnit(Unit unit)
        {
            if (Instance == null || unit == null) return;

            Instance.allUnits.Remove(unit);

            if (unit is PlayerUnit player)
                Instance.playerUnits.Remove(player);
            else if (unit is EnemyUnit enemy)
                Instance.enemyUnits.Remove(enemy);

            OnUnitUnregistered?.Invoke(unit);
        }

        /// <summary>
        /// Notify that a unit has moved. Call this from Unit.SetCurrentTile().
        /// </summary>
        public static void NotifyUnitMoved(Unit unit)
        {
            OnUnitMoved?.Invoke(unit);
        }

        /// <summary>
        /// Notify that a unit has died. Call this from Unit.ReceiveDamage() when health <= 0.
        /// </summary>
        public static void NotifyUnitDied(Unit unit)
        {
            OnUnitDied?.Invoke(unit);
            UnregisterUnit(unit); // Automatically unregister dead units
        }

        /// <summary>
        /// Get all units that can be targeted by the given unit, using cached collection to avoid allocations.
        /// </summary>
        public static IReadOnlyList<Unit> GetValidTargetsFor(Unit caster, System.Func<Unit, bool> additionalFilter = null)
        {
            if (Instance == null || caster == null) return new List<Unit>();

            Instance.cachedValidTargets.Clear();

            foreach (var unit in Instance.allUnits)
            {
                // Skip self
                if (unit == caster) continue;

                // Basic targeting rules
                bool isAlly = (unit is EnemyUnit) == (caster is EnemyUnit);
                if (isAlly) continue; // For now, assume units only target enemies

                // Check if enemy can target this unit (for EnemyUnit targeting restrictions)
                if (caster is EnemyUnit enemy && !enemy.CanTarget(unit)) continue;

                // Apply additional filter if provided
                if (additionalFilter != null && !additionalFilter(unit)) continue;

                Instance.cachedValidTargets.Add(unit);
            }

            return Instance.cachedValidTargets;
        }

        /// <summary>
        /// Get all units within a certain range of a position, using cached collection.
        /// </summary>
        public static IReadOnlyList<Unit> GetUnitsInRange(Vector3 worldPosition, float range)
        {
            if (Instance == null) return new List<Unit>();

            Instance.cachedUnitsInRange.Clear();
            float rangeSqr = range * range;

            foreach (var unit in Instance.allUnits)
            {
                if (unit == null || unit.currentTile == null) continue;

                float distanceSqr = (unit.transform.position - worldPosition).sqrMagnitude;
                if (distanceSqr <= rangeSqr)
                {
                    Instance.cachedUnitsInRange.Add(unit);
                }
            }

            return Instance.cachedUnitsInRange;
        }

        /// <summary>
        /// Find the closest unit to a world position within max distance.
        /// </summary>
        public static Unit GetClosestUnit(Vector3 worldPosition, float maxDistance = float.MaxValue)
        {
            if (Instance == null) return null;

            Unit closest = null;
            float closestDistanceSqr = maxDistance * maxDistance;

            foreach (var unit in Instance.allUnits)
            {
                if (unit == null || unit.currentTile == null) continue;

                float distanceSqr = (unit.transform.position - worldPosition).sqrMagnitude;
                if (distanceSqr < closestDistanceSqr)
                {
                    closestDistanceSqr = distanceSqr;
                    closest = unit;
                }
            }

            return closest;
        }

        /// <summary>
        /// Get count of units by type for quick checks.
        /// </summary>
        public static int GetUnitCount<T>() where T : Unit
        {
            if (Instance == null) return 0;

            if (typeof(T) == typeof(PlayerUnit))
                return Instance.playerUnits.Count;
            else if (typeof(T) == typeof(EnemyUnit))
                return Instance.enemyUnits.Count;
            else
                return Instance.allUnits.Count;
        }

        /// <summary>
        /// Check if any units of a specific type exist.
        /// </summary>
        public static bool HasUnitsOfType<T>() where T : Unit
        {
            return GetUnitCount<T>() > 0;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Debug method to validate unit registration
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugLogUnitCounts()
        {
            Debug.Log($"UnitManager: Total Units: {allUnits.Count}, Players: {playerUnits.Count}, Enemies: {enemyUnits.Count}");
        }
    }
}