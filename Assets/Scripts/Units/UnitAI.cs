using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitAI : MonoBehaviour
    {
        #region Enums

        [System.Serializable]
        public enum AIPersonality { Aggressive, Defensive, Supportive, Balanced }

        #endregion

        #region Inspector Settings

        [Header("AI Configuration")]
        [Range(1, 10)] [SerializeField] private int difficulty = 5;
        [SerializeField] private AIPersonality personality = AIPersonality.Balanced;
        [SerializeField] private int teamId = 1;
        [SerializeField] private bool hostileToAllNonTeam = false;

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool logDetailedScoring = false;
        [SerializeField] private int topActionsToLog = 3;

        [Header("Timing")]
        [SerializeField] private float thinkingDelay = 1.5f;
        [SerializeField] private float actionDelay = 0.8f;
        [SerializeField] private float endTurnDelay = 1.0f;

        #endregion

        #region Private Fields

        private Unit unit;
        private TurnManager turnManager;
        private JumpSystem jumpSystem;

        private AIDebugLogger logger;
        private AIEvaluator evaluator;
        private AIExecutor executor;

        // Targeting overrides — shared state that target management methods modify
        private Dictionary<Unit, int> untargetableUnits = new Dictionary<Unit, int>();
        private Dictionary<Unit, int> targetLikelyUnits = new Dictionary<Unit, int>();

        // Team coordination — static, shared across all AI instances
        private static Dictionary<int, List<UnitAI>> teamGroups = new Dictionary<int, List<UnitAI>>();

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            unit = GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"UnitAI on {gameObject.name} requires a Unit component!");
                enabled = false;
                return;
            }

            RegisterWithTeam();
            TurnManager.OnTurnStarted += OnTurnStarted;
            TurnManager.OnTurnEnded += OnTurnEnded;

            if (enableDebugLogging)
                Debug.Log($"[{gameObject.name}] UnitAI initialized");
        }

        void Start()
        {
            turnManager = FindAnyObjectByType<TurnManager>();
            jumpSystem = FindAnyObjectByType<JumpSystem>();

            // Construct helpers — each gets only what it needs
            logger = new AIDebugLogger(unit, enableDebugLogging, logDetailedScoring, topActionsToLog);
            evaluator = new AIEvaluator(unit, difficulty, personality, CanTargetUnit, IsAlly, logger);

            executor = GetComponent<AIExecutor>();
            if (executor == null)
            {
                Debug.LogError($"[{gameObject.name}] AIExecutor component is missing. Add it to the same prefab as UnitAI.");
                enabled = false;
                return;
            }
            executor.Initialize(unit, jumpSystem, logger);
        }

        void OnDestroy()
        {
            UnregisterFromTeam();
            TurnManager.OnTurnStarted -= OnTurnStarted;
            TurnManager.OnTurnEnded -= OnTurnEnded;
        }

        #endregion

        #region Team Management

        private void RegisterWithTeam()
        {
            if (!teamGroups.ContainsKey(teamId))
                teamGroups[teamId] = new List<UnitAI>();
            if (!teamGroups[teamId].Contains(this))
                teamGroups[teamId].Add(this);
        }

        private void UnregisterFromTeam()
        {
            if (teamGroups.ContainsKey(teamId))
            {
                teamGroups[teamId].Remove(this);
                if (teamGroups[teamId].Count == 0)
                    teamGroups.Remove(teamId);
            }
        }

        private List<UnitAI> GetTeammates() =>
            teamGroups.ContainsKey(teamId)
                ? teamGroups[teamId].Where(ai => ai != this && ai != null).ToList()
                : new List<UnitAI>();

        #endregion

        #region Turn Orchestration

        private void OnTurnStarted(Unit _unit)
        {
            if (_unit == unit)
                StartCoroutine(ExecuteAITurn());
        }

        private void OnTurnEnded(Unit _unit)
        {
            if (_unit == unit)
                UpdateTargetingDurations();
        }

        private IEnumerator ExecuteAITurn()
        {
            logger.LogTurnStart();

            yield return new WaitForSeconds(thinkingDelay);

            logger.LogEvaluationStart();
            var targets = GetPotentialTargets();
            var teammates = GetTeammates();
            var evaluatedActions = evaluator.EvaluateAllActions(targets, teammates);
            logger.LogEvaluationResults(evaluatedActions);

            var selectedPlan = evaluator.SelectBestAction(evaluatedActions);
            logger.LogSelectedAction(selectedPlan);

            if (selectedPlan != null)
                yield return StartCoroutine(executor.ExecuteActionPlan(selectedPlan, actionDelay));
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

        #region Target Management

        public List<Unit> GetPotentialTargets()
        {
            var targets = new List<Unit>();
            foreach (var targetUnit in UnitManager.AllUnits)
            {
                if (targetUnit is EnemyUnit) continue;
                if (!ShouldTargetUnit(targetUnit)) continue;
                targets.Add(targetUnit);
            }
            return targets;
        }

        private bool ShouldTargetUnit(Unit targetUnit)
        {
            if (targetUnit is EnemyUnit enemyTarget)
            {
                var targetAI = enemyTarget.GetComponent<UnitAI>();
                if (targetAI != null && targetAI.teamId == teamId) return false;
            }
            return hostileToAllNonTeam || targetUnit is PlayerUnit;
        }

        public bool CanTargetUnit(Unit target) =>
            unit.CanTarget(target) && !untargetableUnits.ContainsKey(target);

        public bool IsAlly(Unit testUnit)
        {
            if (testUnit is EnemyUnit enemyUnit)
            {
                var enemyAI = enemyUnit.GetComponent<UnitAI>();
                return enemyAI != null && enemyAI.teamId == teamId;
            }
            return false;
        }

        public void AddUntargetableUnit(Unit targetUnit, int duration)
        {
            if (targetUnit == null) return;
            if (untargetableUnits.ContainsKey(targetUnit))
                untargetableUnits[targetUnit] = Mathf.Max(untargetableUnits[targetUnit], duration);
            else
                untargetableUnits[targetUnit] = duration;
        }

        public void AddTargetLikelyUnit(Unit targetUnit, int duration)
        {
            if (targetUnit == null) return;
            if (targetLikelyUnits.ContainsKey(targetUnit))
                targetLikelyUnits[targetUnit] = Mathf.Max(targetLikelyUnits[targetUnit], duration);
            else
                targetLikelyUnits[targetUnit] = duration;
        }

        public bool IsLikelyTarget(Unit targetUnit) =>
            targetUnit != null && targetLikelyUnits.ContainsKey(targetUnit);

        private void UpdateTargetingDurations()
        {
            DecrementDurations(untargetableUnits);
            DecrementDurations(targetLikelyUnits);
        }

        private static void DecrementDurations(Dictionary<Unit, int> dict)
        {
            var toRemove = new List<Unit>();
            foreach (var key in new List<Unit>(dict.Keys))
            {
                if (key == null) { toRemove.Add(key); continue; }
                dict[key]--;
                if (dict[key] <= 0) toRemove.Add(key);
            }
            foreach (var key in toRemove) dict.Remove(key);
        }

        #endregion

        #region Public Properties

        public int Difficulty => difficulty;
        public AIPersonality Personality => personality;
        public int TeamId => teamId;
        public bool HostileToAllNonTeam => hostileToAllNonTeam;

        #endregion
    }
}