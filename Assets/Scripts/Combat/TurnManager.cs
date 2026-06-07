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
        private CameraController cameraController;

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
            cameraController = FindAnyObjectByType<CameraController>();
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
            // Use UnitManager as the single source of truth for the unit list.
            // UnitManager.Awake runs RegisterExistingUnits() before TurnManager.Start,
            // so AllUnits is already populated here. Script Execution Order in Project
            // Settings should place UnitManager before TurnManager to make this explicit.
            var validUnits = UnitManager.AllUnits.Where(u => u is PlayerUnit || u is EnemyUnit);

            // Roll initiative
            var rolled = validUnits.Select(u => new
            {
                unit = u,
                initiative = Random.Range(0, u.currentSpeed + 1)
            });

            // Sort by initiative, break ties randomly
            var sorted = rolled
                .OrderByDescending(x => x.initiative)
                .ThenBy(x => Random.value)
                .Select(x => x.unit)
                .ToList();

            // Stable partition: goesFirst units keep their relative initiative order at the front
            turnOrder = sorted.Where(u => u.goesFirst)
                .Concat(sorted.Where(u => !u.goesFirst))
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

            // Check for stun BEFORE firing any events or starting camera transitions.
            // Handling it here avoids coroutine conflicts between WaitForCameraTransition
            // and EndTurnSequence that occur when stun is processed mid-event-dispatch.
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Stunned))
            {
                StartCoroutine(HandleStunnedTurn(current));
                return;
            }

            // Unit.StartTurn() calls unitAnimator.SetActiveTurn() internally — no direct
            // animator call needed here. The virtual dispatch handles PlayerUnit and EnemyUnit.
            current.StartTurn();

            // FIRE THE EVENTS
            // CombatManager subscribes to OnTurnStarted directly -- no separate direct call needed.
            OnTurnStarted?.Invoke(current);
            OnTotalTurnChanged?.Invoke(totalTurnCount);
        }

        public void EndTurn()
        {
            Unit currentUnit = CurrentUnit;

            // This forces the UI to collapse abilities and prepare for next unit
            if (currentUnit is PlayerUnit)
            {
                UIEvents.OnTurnChanged();
            }

            // Notify current unit that turn is ending. Unit.EndTurn() calls
            // unitAnimator.SetInactiveTurn() internally — no direct animator call needed here.
            currentUnit?.EndTurn();

            // FIRE THE TURN ENDED EVENT
            OnTurnEnded?.Invoke(currentUnit);

            // The rest of the sequence (index advancement, environment effects, next turn start)
            // runs as a coroutine so TriggerEnvironmentEffects can yield on animations.
            StartCoroutine(EndTurnSequence());
        }

        private IEnumerator HandleStunnedTurn(Unit unit)
        {
            Debug.Log($"[TurnManager] {unit.name} is stunned - skipping turn");

            // Pan camera to the stunned unit so the player can see whose turn it is.
            if (cameraController != null)
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));

            yield return new WaitForSeconds(3f);

            // Fire turn ended so status effect durations tick correctly.
            OnTurnEnded?.Invoke(unit);
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