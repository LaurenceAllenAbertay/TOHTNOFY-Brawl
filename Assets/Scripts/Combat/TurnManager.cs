using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class TurnManager : MonoBehaviour
    {
        public static event System.Action<Unit> OnTurnStarted;  // When a unit's turn begins
        public static event System.Action<Unit> OnTurnEnded;    // When a unit's turn ends
        public static event System.Action<int, int> OnTurnNumberChanged; // When turn index changes
        public static event System.Action<int> OnTotalTurnChanged; // When total turn count changes

        private List<Unit> turnOrder = new List<Unit>();
        private int currentIndex = 0;
        [SerializeField] private int totalTurnCount = 0;
        public TextMeshProUGUI turnOrderText;

        // Reference to CombatManager for handling combat actions
        private CombatManager combatManager;

        // Properties
        public Unit CurrentUnit => turnOrder.Count > 0 ? turnOrder[currentIndex] : null;
        public List<Unit> TurnOrder => turnOrder;
        public int CurrentTurnIndex => currentIndex;
        public int TotalTurnCount => totalTurnCount;

        // Snapshotted at the start of TriggerEnvironmentEffects so GetCurrentRound() returns a
        // stable value for the entire environment-effect phase, even if units die mid-processing.
        // 0 means "not currently in environment effects — use live turnOrder.Count".
        private int _stableRoundCount = 0;

        void Start()
        {
            combatManager = FindAnyObjectByType<CombatManager>();
            BuildTurnOrder();
            StartNextTurn();
        }

        void OnEnable()
        {
            UnitManager.OnUnitDied += HandleUnitDied;
        }

        void OnDisable()
        {
            UnitManager.OnUnitDied -= HandleUnitDied;
        }

        /// <summary>
        /// Removes a dead unit from the turn order and adjusts currentIndex so the
        /// next call to StartNextTurn() lands on the correct unit.
        /// Called by UnitManager.OnUnitDied for every death source: abilities, tile effects, anything.
        /// </summary>
        private void HandleUnitDied(Unit unit)
        {
            int diedIndex = turnOrder.IndexOf(unit);
            if (diedIndex < 0) return; // Not in our turn order

            turnOrder.RemoveAt(diedIndex);

            // If the dead unit was at or before the current index, shift the index back
            // so it still points at the same logical "next" unit after removal.
            if (diedIndex <= currentIndex)
                currentIndex = Mathf.Max(0, currentIndex - 1);

            // Clamp to valid range in case the list is now shorter
            if (turnOrder.Count > 0)
                currentIndex = Mathf.Clamp(currentIndex, 0, turnOrder.Count - 1);
        }

        void BuildTurnOrder()
        {
            var allUnits = FindObjectsByType<Unit>(FindObjectsSortMode.None);
            // Only include PlayerUnit and EnemyUnit
            var validUnits = allUnits.Where(u => u is PlayerUnit || u is EnemyUnit);

            // Roll initiative
            var rolled = validUnits.Select(u => new
            {
                unit = u,
                initiative = Random.Range(0, u.currentSpeed + 1)
            });

            // Sort by initiative, break ties randomly
            turnOrder = rolled
                .OrderByDescending(x => x.initiative)
                .ThenBy(x => Random.value)
                .Select(x => x.unit)
                .ToList();

            // Debug Mode
            if (turnOrderText == null) return;
            turnOrderText.text = "Turn Order:";
            foreach (var unit in turnOrder)
            {
                turnOrderText.text += $"\n- {unit.gameObject.name}";
            }
        }

        void StartNextTurn()
        {
            if (turnOrder.Count == 0)
            {
                Debug.LogWarning("TurnManager: No units in turn order!");
                return;
            }

            // Increment total turn count at the start of each turn
            totalTurnCount++;

            Unit current = CurrentUnit;

            var unitAnimator = current.GetComponent<UnitAnimator>();
            if (unitAnimator != null)
            {
                unitAnimator.SetActiveTurn();
            }

            if (current is PlayerUnit player)
                player.StartTurn();
            else if (current is EnemyUnit enemy)
                enemy.StartTurn();

            // Notify CombatManager about the new active unit
            if (combatManager != null)
                combatManager.OnTurnStarted(current);

            // FIRE THE EVENTS
            OnTurnStarted?.Invoke(current);
            OnTotalTurnChanged?.Invoke(totalTurnCount);
        }

        public void EndTurn()
        {
            Unit currentUnit = CurrentUnit;

            var unitAnimator = currentUnit?.GetComponent<UnitAnimator>();
            if (unitAnimator != null)
            {
                unitAnimator.SetInactiveTurn();
            }

            // This forces the UI to collapse abilities and prepare for next unit
            if (currentUnit is PlayerUnit)
            {
                UIEvents.OnTurnChanged();
            }

            // Notify current unit that turn is ending
            currentUnit?.EndTurn();

            // FIRE THE TURN ENDED EVENT
            OnTurnEnded?.Invoke(currentUnit);

            // The rest of the sequence (index advancement, environment effects, next turn start)
            // runs as a coroutine so TriggerEnvironmentEffects can yield on animations.
            StartCoroutine(EndTurnSequence());
        }

        private IEnumerator EndTurnSequence()
        {
            currentIndex++;
            if (currentIndex >= turnOrder.Count)
            {
                currentIndex = 0;
                // Snapshot the turn order size before any tile-effect deaths can shrink it.
                // GetCurrentRound() uses this value for the entire environment-effect phase,
                // so round numbers stay stable for UI and any round-scaling logic.
                _stableRoundCount = turnOrder.Count;
                yield return StartCoroutine(TriggerEnvironmentEffects());
                _stableRoundCount = 0; // Back to live count after effects finish
            }

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);

            // Guard: if all units died during environment effects, stop here
            if (turnOrder.Count == 0) yield break;

            StartNextTurn();
        }

        private IEnumerator TriggerEnvironmentEffects()
        {
            if (GridManager.Instance == null) yield break;

            int currentRound = GetCurrentRound();

            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile.HasActiveEffects)
                    yield return StartCoroutine(tile.TriggerEffects(currentRound));
            }
        }

        // Public method to reset total turn count (useful for new battles)
        public void ResetTotalTurnCount()
        {
            totalTurnCount = 0;
        }

        // Public method to get current round number (how many complete cycles through all units)
        public int GetCurrentRound()
        {
            // During TriggerEnvironmentEffects, use the snapshotted count so deaths mid-processing
            // don't silently change the round number for UI or round-scaling damage effects.
            int count = _stableRoundCount > 0 ? _stableRoundCount : turnOrder.Count;
            if (count == 0) return 0;
            return (totalTurnCount - 1) / count + 1;
        }

        // Public method to get turn within current round (1-based)
        public int GetTurnInCurrentRound()
        {
            // Uses the same stable snapshot as GetCurrentRound() so both methods return
            // consistent values during the environment-effect phase, even if units die mid-processing.
            int count = _stableRoundCount > 0 ? _stableRoundCount : turnOrder.Count;
            if (count == 0) return 0;
            return ((totalTurnCount - 1) % count) + 1;
        }
    }
}