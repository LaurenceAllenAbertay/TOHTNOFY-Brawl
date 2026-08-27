using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitAI : MonoBehaviour
    {
        [Header("AI Behaviour")]
        [Range(0f, 1f)]
        [SerializeField] private float aggressionBias = 1f;
        
        [Range(1, 2)]
        [SerializeField] private int lookAheadSteps = 1;
        
        [Min(1)]
        [SerializeField] private int targetCandidateCount = 3;

        [Header("Timing")]
        [SerializeField] private float thinkingDelay = 1.5f;
        
        [SerializeField] private float actionDelay = 0.8f;

        [SerializeField] private float endTurnDelay = 1.0f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool logDetailedScoring = false;
        [SerializeField] private int  topActionsToLog    = 3;

        private Unit         unit;
        private TurnManager  turnManager;
        private JumpSystem   jumpSystem;

        private AIDebugLogger logger;
        private AIPlanner     planner;
        private AIExecutor    executor;
        
        private readonly Dictionary<Unit, int> untargetableUnits  = new Dictionary<Unit, int>();
        private readonly Dictionary<Unit, int> targetLikelyUnits  = new Dictionary<Unit, int>();
        
        private Unit lastAttacker;
        
        private static readonly Dictionary<int, List<UnitAI>> teamGroups
            = new Dictionary<int, List<UnitAI>>();
        
        private void Awake()
        {
            unit = GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"[UnitAI] {gameObject.name} requires a Unit component.");
                enabled = false;
                return;
            }

            unit.EnsureTeamResolved();
            RegisterWithTeam();

            TurnManager.OnTurnStarted                 += OnTurnStarted;
            TurnManager.OnTurnEnded                   += OnTurnEnded;
            StatusEffectManager.OnStatusEffectApplied += OnStatusEffectApplied;
            UnitManager.OnUnitDamaged                 += OnUnitDamaged;
            UnitManager.OnUnitDied                    += OnUnitDied;
        }

        private void Start()
        {
            turnManager = FindAnyObjectByType<TurnManager>();
            jumpSystem  = FindAnyObjectByType<JumpSystem>();

            logger  = new AIDebugLogger(unit, enableDebugLogging, logDetailedScoring, topActionsToLog);
            planner = new AIPlanner(unit, aggressionBias, lookAheadSteps, targetCandidateCount, CanTargetUnit, IsAlly, GetTeammateUnits, GetLastAttacker, logger);

            executor = GetComponent<AIExecutor>();
            if (executor == null)
            {
                Debug.LogError($"[UnitAI] {gameObject.name} is missing an AIExecutor component.");
                enabled = false;
                return;
            }
            executor.Initialize(unit, jumpSystem, logger);
        }

        private void OnDestroy()
        {
            UnregisterFromTeam();
            TurnManager.OnTurnStarted                 -= OnTurnStarted;
            TurnManager.OnTurnEnded                   -= OnTurnEnded;
            StatusEffectManager.OnStatusEffectApplied -= OnStatusEffectApplied;
            UnitManager.OnUnitDamaged                 -= OnUnitDamaged;
            UnitManager.OnUnitDied                    -= OnUnitDied;
        }
        
        private void OnTurnStarted(Unit activeUnit)
        {
            if (activeUnit != unit) return;
            if (!unit.IsAIControlled) return;

            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Dizzy))
                return;

            StartCoroutine(ExecuteAITurn());
        }

        private void OnTurnEnded(Unit activeUnit)
        {
            if (activeUnit == unit)
                TickTargetingDurations();
        }

        private IEnumerator ExecuteAITurn()
        {
            logger.LogTurnStart();

            if (unit.pendingAction.HasValue)
            {
                if (!unit.CanUseAbilities())
                {
                    unit.pendingAction = null;
                    Debug.Log($"[UnitAI] {unit.name} is scared — pending action cleared.");
                }
                else
                {
                    var pending      = unit.pendingAction.Value;
                    unit.pendingAction = null;

                    Debug.Log($"[UnitAI] {unit.name} executing queued action: {pending.ability.abilityName}");

                    var ctx = new AbilityContext
                    {
                        caster   = unit,
                        ability  = pending.ability,
                        aimDir   = pending.aimDir
                    };
                    yield return StartCoroutine(unit.ExecuteAbilityCoroutine(ctx));
                    yield return new WaitForSeconds(endTurnDelay);
                    turnManager.EndTurn();
                    yield break;
                }
            }

            yield return new WaitForSeconds(thinkingDelay);

            var targets      = GetTargets();
            logger.LogTargetsAndTeammates(targets, new List<UnitAI>());

            var plan = planner.Plan(targets);
            logger.LogSelectedAction(plan);

            if (plan != null)
                yield return StartCoroutine(executor.ExecuteActionPlan(plan, actionDelay));
            else
            {
                logger.LogNoValidActions();
                yield return new WaitForSeconds(0.5f);
            }

            logger.LogTurnEnd();
            yield return new WaitForSeconds(endTurnDelay);
            turnManager.EndTurn();
        }

        private List<Unit> GetTargets()
        {
            var targets = new List<Unit>();

            foreach (var candidate in UnitManager.AllUnits)
            {
                if (candidate == null) continue;
                if (candidate.IsDead || candidate.IsNeutral) continue;
                if (!ShouldTarget(candidate)) continue;
                if (!CanTargetUnit(candidate)) continue;
                targets.Add(candidate);
            }
            
            var taunting = targets.Where(t => targetLikelyUnits.ContainsKey(t)).ToList();
            if (taunting.Count > 0) return taunting;

            return targets;
        }

        private bool ShouldTarget(Unit candidate)
        {
            if (candidate == unit) return false;
            
            if (candidate.IsDead) return false;

            if (candidate.IsNeutral) return false;
            
            if (IsAlly(candidate)) return false;

            return true;
        }
        
        public bool CanTargetUnit(Unit target)
            => unit.CanTarget(target) && !untargetableUnits.ContainsKey(target);

        public bool IsAlly(Unit candidate)
        {
            if (candidate == null) return false;
            return unit.IsAllyOf(candidate);
        }

        public void AddUntargetableUnit(Unit target, int duration)
        {
            if (target == null) return;
            if (untargetableUnits.TryGetValue(target, out int existing))
                untargetableUnits[target] = Mathf.Max(existing, duration);
            else
                untargetableUnits[target] = duration;
        }
        
        public void AddTargetLikelyUnit(Unit target, int duration)
        {
            if (target == null) return;
            if (targetLikelyUnits.TryGetValue(target, out int existing))
                targetLikelyUnits[target] = Mathf.Max(existing, duration);
            else
                targetLikelyUnits[target] = duration;
        }

        private void TickTargetingDurations()
        {
            DecrementAndClean(untargetableUnits);
            DecrementAndClean(targetLikelyUnits);
        }

        private static void DecrementAndClean(Dictionary<Unit, int> dict)
        {
            var keys = new List<Unit>(dict.Keys);
            foreach (var key in keys)
            {
                dict[key]--;
                if (dict[key] <= 0)
                    dict.Remove(key);
            }
        }
        
        private void OnStatusEffectApplied(Unit target, StatusEffectInstance effect)
        {
            if (effect.effectData.effectType == StatusEffectType.Taunting)
                AddTargetLikelyUnit(target, effect.remainingDuration);
        }
        
        private void OnUnitDamaged(Unit victim, Unit attacker)
        {
            if (victim != unit) return;
            if (attacker == null || attacker == unit) return;
            if (!ShouldTarget(attacker)) return;
            lastAttacker = attacker;
        }

        private void OnUnitDied(Unit dead)
        {
            if (lastAttacker == dead)
                lastAttacker = null;
        }
        
        private Unit GetLastAttacker() => lastAttacker;
        
        public void RegisterWithTeam()
        {
            int team = unit.team;
            if (!teamGroups.ContainsKey(team))
                teamGroups[team] = new List<UnitAI>();
            if (!teamGroups[team].Contains(this))
                teamGroups[team].Add(this);
        }

        public void UnregisterFromTeam()
        {
            int team = unit.team;
            if (!teamGroups.ContainsKey(team)) return;
            teamGroups[team].Remove(this);
            if (teamGroups[team].Count == 0)
                teamGroups.Remove(team);
        }

        private List<Unit> GetTeammateUnits()
        {
            var result = new List<Unit>();
            if (!teamGroups.TryGetValue(unit.team, out var teammates)) return result;
            foreach (var ai in teammates)
            {
                if (ai == this || ai.unit == null || ai.unit.IsDead) continue;
                result.Add(ai.unit);
            }
            return result;
        }
    }
}