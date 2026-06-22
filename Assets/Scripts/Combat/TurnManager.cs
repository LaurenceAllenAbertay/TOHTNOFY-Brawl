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

        // Set when the unit whose turn it currently is dies during their own turn (e.g. recoil
        // damage). EndTurn() checks this flag so it can skip calling CurrentUnit?.EndTurn() —
        // which would incorrectly target whoever inherited currentIndex after the removal — and
        // instead just advances the sequence. Cleared at the start of every new turn.
        private bool _activeUnitDiedThisTurn = false;

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

            // If the unit that just died is the one currently taking their turn, flag it.
            // EndTurn() will skip calling CurrentUnit?.EndTurn() (which would land on the
            // wrong unit after the index shifts) and will just advance the sequence instead.
            if (diedIndex == currentIndex)
                _activeUnitDiedThisTurn = true;

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
            // NeutralUnits are intentionally excluded — they act during the environment turn,
            // not via a normal turn slot.
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

            // Clear the self-death flag from the previous turn. A fresh unit is now active
            // so any death that occurs from here belongs to a different context.
            _activeUnitDiedThisTurn = false;

            Unit current = CurrentUnit;

            // Check for turn-skipping status effects BEFORE firing any events or starting
            // camera transitions. Handling here avoids coroutine conflicts between
            // WaitForCameraTransition and EndTurnSequence that occur when these are processed
            // mid-event-dispatch. Shocked is checked after Stunned — both use the same
            // HandleStunnedTurn coroutine since the visual behaviour is identical.
            if (StatusEffectManager.Instance != null &&
                (StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Stunned) ||
                 StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Shocked)))
            {
                StartCoroutine(HandleStunnedTurn(current));
                return;
            }

            // Dizzy: unit fires a random ability at a random target instead of normal input.
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Dizzy))
            {
                StartCoroutine(HandleDizzyTurn(current));
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
            // If the active unit died during their own turn (e.g. recoil damage), currentIndex
            // has already been adjusted by HandleUnitDied to point at the next unit. Calling
            // CurrentUnit?.EndTurn() here would incorrectly end the *next* unit's turn before
            // it has even started.
            //
            // We also skip firing OnTurnEnded — the dead unit's status effects were already
            // cleared by StatusEffectManager.HandleUnitDied, so duration ticking is not needed,
            // and passing null would throw NullReferenceExceptions in handlers that use the
            // unit as a dictionary key (e.g. StatusEffectManager, LoneWolfPassive).
            if (_activeUnitDiedThisTurn)
            {
                Debug.Log("[TurnManager] Active unit died during their own turn — skipping EndTurn calls and advancing sequence.");
                StartCoroutine(EndTurnSequence());
                return;
            }

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

        private IEnumerator HandleDizzyTurn(Unit unit)
        {
            Debug.Log($"[TurnManager] {unit.name} is dizzy — firing random ability.");

            // Pan camera to the dizzy unit.
            if (cameraController != null)
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));

            // Brief pause so the player can register whose turn it is.
            yield return new WaitForSeconds(1f);

            // Fire turn-start events so status effects (including Dizzy's duration tick
            // at end-of-turn) process correctly, and so UI hides for the dummy turn.
            unit.StartTurn();
            OnTurnStarted?.Invoke(unit);

            // Collect non-null abilities from the unit's loadout.
            var loadout = UnitLoadoutManager.GetAbilities(unit);
            if (loadout == null || loadout.Length == 0)
            {
                Debug.Log($"[Dizzy] {unit.name} has no abilities — skipping.");
                OnTurnEnded?.Invoke(unit);
                StartCoroutine(EndTurnSequence());
                yield break;
            }

            // Shuffle the loadout order so even the ability choice feels random.
            var shuffled = new System.Collections.Generic.List<Ability>(loadout);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }

            // Try each ability in shuffled order until one executes successfully.
            bool fired = false;
            Vector2Int[] cardinalDirs = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

            foreach (var ability in shuffled)
            {
                if (ability == null) continue;

                // Shuffle directions for directional abilities.
                var dirs = new System.Collections.Generic.List<Vector2Int>(cardinalDirs);
                for (int i = dirs.Count - 1; i > 0; i--)
                {
                    int j = UnityEngine.Random.Range(0, i + 1);
                    (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
                }

                foreach (var dir in dirs)
                {
                    if (!ability.CanExecute(unit, dir)) continue;

                    Debug.Log($"[Dizzy] {unit.name} fires {ability.abilityName} in direction {dir}.");
                    var ctx = new AbilityContext { caster = unit, ability = ability, aimDir = dir };
                    yield return StartCoroutine(unit.ExecuteAbilityCoroutine(ctx));

                    // Drain any deaths that occurred during the random ability.
                    if (UnitDeathSequencer.Instance != null)
                        yield return StartCoroutine(UnitDeathSequencer.Instance.DrainDeathQueue(unit));

                    fired = true;
                    break;
                }

                if (fired) break;
            }

            if (!fired)
                Debug.Log($"[Dizzy] {unit.name} couldn't find a valid random ability to fire.");

            unit.EndTurn();
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

            // ── Phase 1: Tile effects ─────────────────────────────────────────
            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile.HasActiveEffects)
                    yield return StartCoroutine(tile.TriggerEffects(currentRound));
            }

            // Drain deaths caused by tile effects before neutral units act.
            if (UnitDeathSequencer.Instance != null)
                yield return StartCoroutine(UnitDeathSequencer.Instance.DrainDeathQueue(returnToUnit: null));

            // ── Phase 2: Neutral unit environment abilities ────────────────────
            // Take a snapshot of the list before iterating — a neutral unit's environmentAbility
            // could theoretically kill itself (e.g. a bomb with 1 HP), which would modify
            // AllNeutralUnits mid-iteration if we read from it directly.
            var neutralSnapshot = new List<NeutralUnit>(UnitManager.AllNeutralUnits);
            foreach (var neutral in neutralSnapshot)
            {
                // Skip any that died during tile effects (or earlier this same pass).
                if (neutral == null || neutral.IsDead) continue;
                if (neutral.environmentAbility == null) continue;

                // Pan camera to the neutral unit so the player can see it acting.
                if (cameraController != null)
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(neutral)));

                // Build a context and execute the environment ability.
                // aimDir is not meaningful for self/AOE abilities; use current facing as default.
                var ctx = new AbilityContext
                {
                    caster = neutral,
                    ability = neutral.environmentAbility,
                    aimDir  = neutral.currentFacing
                };

                yield return StartCoroutine(neutral.ExecuteAbilityCoroutine(ctx));

                // Drain any deaths that occurred during this neutral's ability before
                // moving on to the next one so the camera sequencing stays clean.
                if (UnitDeathSequencer.Instance != null)
                    yield return StartCoroutine(UnitDeathSequencer.Instance.DrainDeathQueue(returnToUnit: null));
            }

            // Final drain — catches any stragglers (e.g. a neutral unit that died to its
            // own destructionAbility trigger during the environment pass).
            if (UnitDeathSequencer.Instance != null)
                yield return StartCoroutine(UnitDeathSequencer.Instance.DrainDeathQueue(returnToUnit: null));
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