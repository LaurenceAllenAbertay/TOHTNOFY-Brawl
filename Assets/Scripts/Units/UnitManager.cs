using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitManager : MonoBehaviour
    {
        public static UnitManager Instance { get; private set; }

        public static event System.Action<Unit> OnUnitRegistered;
        public static event System.Action<Unit> OnUnitUnregistered;
        public static event System.Action<Unit> OnUnitMoved;
        public static event System.Action<Unit> OnUnitDied;

        public static event System.Action<Unit, Unit> OnUnitDamaged;

        public static event System.Action<Unit> OnBodySpawned;

        private readonly List<Unit> allUnits = new List<Unit>();
        private readonly List<PlayerUnit> playerUnits = new List<PlayerUnit>();
        private readonly List<NpcUnit> npcUnits = new List<NpcUnit>();

        private readonly List<NeutralUnit> neutralUnits = new List<NeutralUnit>();

        private readonly List<Unit> bodyUnits = new List<Unit>();

        private readonly List<Unit> cachedValidTargets = new List<Unit>();
        private readonly List<Unit> cachedUnitsInRange = new List<Unit>();

        public static IReadOnlyList<Unit> AllUnits => Instance?.allUnits ?? new List<Unit>();
        public static IReadOnlyList<PlayerUnit> PlayerUnits => Instance?.playerUnits ?? new List<PlayerUnit>();
        public static IReadOnlyList<NpcUnit> NpcUnits => Instance?.npcUnits ?? new List<NpcUnit>();

        public static IReadOnlyList<NeutralUnit> AllNeutralUnits => Instance?.neutralUnits ?? new List<NeutralUnit>();

        public static IReadOnlyList<Unit> AllBodies => Instance?.bodyUnits ?? new List<Unit>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            RegisterExistingUnits();
        }

        private void RegisterExistingUnits()
        {
            var existingUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
            foreach (var unit in existingUnits)
            {
                if (!allUnits.Contains(unit))
                {
                    RegisterUnit(unit);
                }
            }
        }

        public static void RegisterUnit(Unit unit)
        {
            if (Instance == null || unit == null) return;

            if (Instance.allUnits.Contains(unit)) return;

            Instance.allUnits.Add(unit);

            if (unit is PlayerUnit player && !Instance.playerUnits.Contains(player))
                Instance.playerUnits.Add(player);
            else if (unit is NpcUnit npc && !Instance.npcUnits.Contains(npc))
                Instance.npcUnits.Add(npc);
            else if (unit is NeutralUnit neutral && !Instance.neutralUnits.Contains(neutral))
                Instance.neutralUnits.Add(neutral);

            OnUnitRegistered?.Invoke(unit);
        }

        public static void UnregisterUnit(Unit unit)
        {
            if (Instance == null || unit == null) return;

            Instance.allUnits.Remove(unit);
            Instance.bodyUnits.Remove(unit);

            if (unit is PlayerUnit player)
                Instance.playerUnits.Remove(player);
            else if (unit is NpcUnit npc)
                Instance.npcUnits.Remove(npc);
            else if (unit is NeutralUnit neutral)
                Instance.neutralUnits.Remove(neutral);

            OnUnitUnregistered?.Invoke(unit);
        }

        public static void NotifyUnitMoved(Unit unit)
        {
            OnUnitMoved?.Invoke(unit);
        }

        public static void NotifyUnitDamaged(Unit victim, Unit attacker)
        {
            OnUnitDamaged?.Invoke(victim, attacker);
        }

        public static void NotifyUnitDied(Unit unit)
        {
            OnUnitDied?.Invoke(unit);

            if (Instance == null || unit == null) return;

            if (unit is PlayerUnit player)
                Instance.playerUnits.Remove(player);
            else if (unit is NpcUnit npc)
                Instance.npcUnits.Remove(npc);
            else if (unit is NeutralUnit neutral)
            {
                Instance.neutralUnits.Remove(neutral);
            }
        }

        public static void NotifyBodySpawned(Unit unit)
        {
            if (Instance == null || unit == null) return;
            if (!Instance.bodyUnits.Contains(unit))
                Instance.bodyUnits.Add(unit);
            OnBodySpawned?.Invoke(unit);
        }

        public static IReadOnlyList<Unit> GetValidTargetsFor(Unit caster, System.Func<Unit, bool> additionalFilter = null)
        {
            if (Instance == null || caster == null) return new List<Unit>();

            Instance.cachedValidTargets.Clear();

            foreach (var unit in Instance.allUnits)
            {
                if (unit == caster) continue;

                if (caster.IsAllyOf(unit)) continue;

                if (caster is NpcUnit npc && !npc.CanTarget(unit)) continue;

                if (additionalFilter != null && !additionalFilter(unit)) continue;

                Instance.cachedValidTargets.Add(unit);
            }

            return Instance.cachedValidTargets;
        }

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

        public static int GetUnitCount<T>() where T : Unit
        {
            if (Instance == null) return 0;

            if (typeof(T) == typeof(PlayerUnit))
                return Instance.playerUnits.Count;
            else if (typeof(T) == typeof(NpcUnit))
                return Instance.npcUnits.Count;
            else if (typeof(T) == typeof(NeutralUnit))
                return Instance.neutralUnits.Count;
            else
                return Instance.allUnits.Count;
        }

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
    }
}