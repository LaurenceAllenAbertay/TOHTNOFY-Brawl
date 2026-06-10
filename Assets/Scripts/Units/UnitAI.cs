using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Orchestrates an AI unit's turn.
    ///
    /// Responsibilities:
    ///   • Holds all designer-facing configuration.
    ///   • Listens to TurnManager events and launches the turn coroutine.
    ///   • Builds the target list and hands it to AIPlanner.
    ///   • Passes the resulting ActionPlan to AIExecutor.
    ///
    /// What this class does NOT do:
    ///   • Score or evaluate actions — that is AIPlanner's job.
    ///   • Execute movement or abilities — that is AIExecutor's job.
    /// </summary>
    public class UnitAI : MonoBehaviour
    {
        #region Inspector Settings

        [Header("Team")]
        [Tooltip("Units with the same teamId are allies and will not target each other.")]
        [SerializeField] private int teamId = 1;

        [Tooltip("When true this unit is hostile to every unit not on its team, " +
                 "including other enemy teams. When false it only targets PlayerUnits.")]
        [SerializeField] private bool hostileToAllNonTeam = false;

        [Header("AI Behaviour")]
        [Tooltip("0 = cautious (picks the safest tile that still advances toward the target). " +
                 "1 = aggressive (always moves as close to the target as possible). " +
                 "Does not affect ability priority — the unit will always attack when it can.")]
        [Range(0f, 1f)]
        [SerializeField] private float aggressionBias = 1f;

        [Tooltip("1 = score only the current turn. " +
                 "2 = also reward positions that will be in ability range next turn.")]
        [Range(1, 2)]
        [SerializeField] private int lookAheadSteps = 1;

        [Header("Timing")]
        [Tooltip("Pause before the unit begins evaluating — gives the player time to read the board.")]
        [SerializeField] private float thinkingDelay = 1.5f;

        [Tooltip("Short pause between movement and ability execution.")]
        [SerializeField] private float actionDelay = 0.8f;

        [Tooltip("Pause after all actions are complete before the turn formally ends.")]
        [SerializeField] private float endTurnDelay = 1.0f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool logDetailedScoring = false;
        [SerializeField] private int  topActionsToLog    = 3;

        #endregion

        #region Private Fields

        private Unit         unit;
        private TurnManager  turnManager;
        private JumpSystem   jumpSystem;

        private AIDebugLogger logger;
        private AIPlanner     planner;
        private AIExecutor    executor;

        // Per-unit targeting overrides — set externally by status effects (e.g. Taunting).
        private readonly Dictionary<Unit, int> untargetableUnits  = new Dictionary<Unit, int>();
        private readonly Dictionary<Unit, int> targetLikelyUnits  = new Dictionary<Unit, int>();

        // Static team registry — shared across all UnitAI instances so we can cheaply
        // identify allies without a scene search every turn.
        private static readonly Dictionary<int, List<UnitAI>> teamGroups
            = new Dictionary<int, List<UnitAI>>();

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            unit = GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"[UnitAI] {gameObject.name} requires a Unit component.");
                enabled = false;
                return;
            }

            RegisterWithTeam();

            TurnManager.OnTurnStarted           += OnTurnStarted;
            TurnManager.OnTurnEnded             += OnTurnEnded;
            StatusEffectManager.OnStatusEffectApplied += OnStatusEffectApplied;
        }

        private void Start()
        {
            turnManager = FindAnyObjectByType<TurnManager>();
            jumpSystem  = FindAnyObjectByType<JumpSystem>();

            logger  = new AIDebugLogger(unit, enableDebugLogging, logDetailedScoring, topActionsToLog);
            planner = new AIPlanner(unit, aggressionBias, lookAheadSteps, CanTargetUnit, IsAlly, logger);

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
        }

        #endregion

        #region Turn Orchestration

        private void OnTurnStarted(Unit activeUnit)
        {
            if (activeUnit != unit) return;

            // TurnManager already handles Stunned, Shocked, and Dizzy in its own coroutines.
            // Guard here so UnitAI never races against those.
            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Stunned)  ||
                    StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Shocked)  ||
                    StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Dizzy))
                    return;
            }

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

            // A pending action (e.g. set by Chug) takes priority over normal planning.
            if (unit.pendingAction.HasValue)
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

        #endregion

        #region Target List

        /// <summary>
        /// Builds the list of units this AI should consider targeting this turn.
        /// Respects team affiliation, untargetable overrides, and Taunting priority.
        /// </summary>
        private List<Unit> GetTargets()
        {
            var targets = new List<Unit>();

            foreach (var candidate in UnitManager.AllUnits)
            {
                if (candidate == null || candidate.IsDead) continue;
                if (!ShouldTarget(candidate)) continue;
                if (!CanTargetUnit(candidate)) continue;
                targets.Add(candidate);
            }

            // If any unit has Taunting, restrict the target list to them.
            var taunting = targets.Where(t => targetLikelyUnits.ContainsKey(t)).ToList();
            if (taunting.Count > 0) return taunting;

            return targets;
        }

        private bool ShouldTarget(Unit candidate)
        {
            // Never target self.
            if (candidate == unit) return false;

            // Never target dead units.
            if (candidate.IsDead) return false;

            // Never target allies.
            if (IsAlly(candidate)) return false;

            // When hostileToAllNonTeam is false, only PlayerUnits are valid targets.
            if (!hostileToAllNonTeam && !(candidate is PlayerUnit)) return false;

            return true;
        }

        #endregion

        #region Targeting Predicates (passed to AIPlanner)

        /// <summary>True when this unit is permitted to target the given unit this turn.</summary>
        public bool CanTargetUnit(Unit target)
            => unit.CanTarget(target) && !untargetableUnits.ContainsKey(target);

        /// <summary>True when the given unit is an ally (same team, or explicitly allied).</summary>
        public bool IsAlly(Unit candidate)
        {
            if (candidate is EnemyUnit enemy)
            {
                var ai = enemy.GetComponent<UnitAI>();
                return ai != null && ai.teamId == teamId;
            }
            return false;
        }

        #endregion

        #region Targeting Overrides (set by external systems, e.g. StatusEffectManager)

        /// <summary>
        /// Marks a unit as untargetable for the specified number of turns.
        /// If already present, the longer duration is kept.
        /// </summary>
        public void AddUntargetableUnit(Unit target, int duration)
        {
            if (target == null) return;
            if (untargetableUnits.TryGetValue(target, out int existing))
                untargetableUnits[target] = Mathf.Max(existing, duration);
            else
                untargetableUnits[target] = duration;
        }

        /// <summary>
        /// Marks a unit as a priority target (e.g. Taunting) for the specified turns.
        /// </summary>
        public void AddTargetLikelyUnit(Unit target, int duration)
        {
            if (target == null) return;
            if (targetLikelyUnits.TryGetValue(target, out int existing))
                targetLikelyUnits[target] = Mathf.Max(existing, duration);
            else
                targetLikelyUnits[target] = duration;
        }

        /// <summary>Decrements all targeting override durations. Called at end of each turn.</summary>
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

        #endregion

        #region Status Effect Responses

        private void OnStatusEffectApplied(Unit target, StatusEffectInstance effect)
        {
            // When Taunting is applied to any unit, register that unit as a priority target.
            if (effect.effectData.effectType == StatusEffectType.Taunting)
                AddTargetLikelyUnit(target, effect.remainingDuration);
        }

        #endregion

        #region Team Registry

        private void RegisterWithTeam()
        {
            if (!teamGroups.ContainsKey(teamId))
                teamGroups[teamId] = new List<UnitAI>();
            if (!teamGroups[teamId].Contains(this))
                teamGroups[teamId].Add(this);
        }

        private void UnregisterFromTeam()
        {
            if (!teamGroups.ContainsKey(teamId)) return;
            teamGroups[teamId].Remove(this);
            if (teamGroups[teamId].Count == 0)
                teamGroups.Remove(teamId);
        }

        #endregion
    }
}