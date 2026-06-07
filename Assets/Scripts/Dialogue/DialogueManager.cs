using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Singleton MonoBehaviour that drives the in-combat dialogue system.
    ///
    /// Responsibilities
    /// ────────────────
    /// • Subscribes to existing global events (UnitManager) and converts them into
    ///   DialogueTrigger evaluations automatically.
    /// • Exposes a static Trigger() method for code sites that need to fire a trigger
    ///   not covered by an existing global event.
    /// • Selects a speaker from the correct pool (global random / specific unit /
    ///   exclude-instigator).
    /// • For SubsequentAllyDowned: if a ComboDialogueDatabase entry exactly matches
    ///   the current survivors, there is a 50/50 chance the combo sequence plays
    ///   instead of the normal random line. If no combo matches, the normal line
    ///   always plays.
    /// • Applies the DialogueChancePercent roll for non-combo lines.
    /// • Maintains a priority queue so the most important line always plays first;
    ///   equal-priority lines are queued in arrival order.
    /// • Hands each (speaker, line, colour) tuple to DialogueBubbleUI for display.
    ///
    /// Settings
    /// ────────
    /// DialogueChancePercent (default 50):
    ///   • 0   → no dialogue ever plays, even guaranteed lines.
    ///   • 1–99 → non-guaranteed lines play with that % probability;
    ///            guaranteed lines always play.
    ///   • 100  → every trigger always fires.
    ///
    /// TODO: Hook DialogueChancePercent up to a settings slider backed by PlayerPrefs
    ///       once the global GameSettings system is implemented. The field is intentionally
    ///       public so GameSettings can write to it directly.
    /// </summary>
    public class DialogueManager : MonoBehaviour
    {
        // ── Singleton ─────────────────────────────────────────────────────────

        public static DialogueManager Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Settings")]
        [Tooltip("0 = no dialogue ever. 1-99 = chance for non-guaranteed lines. " +
                 "100 = everything fires. Default 50. " +
                 "TODO: drive this from a GameSettings PlayerPrefs slider.")]
        [Range(0f, 100f)]
        public float DialogueChancePercent = 50f;

        [Header("References")]
        [Tooltip("The single DialogueBubbleUI in the scene that will display lines.")]
        [SerializeField] private DialogueBubbleUI bubbleUI;

        [Tooltip("Optional database of context-sensitive multi-speaker combo dialogues. " +
                 "Checked on SubsequentAllyDowned. If a combo matches the current survivors " +
                 "there is a 50/50 chance it plays instead of the normal random line.")]
        [SerializeField] private ComboDialogueDatabase comboDatabase;

        // ── Internal state ────────────────────────────────────────────────────

        // Tracks whether the very first player-unit death has occurred this combat.
        private bool _firstAllyDownedThisCombat = false;

        // Tracks which enemies have already had their sub-50% line fired this combat
        // so it only ever fires once per enemy.
        private readonly HashSet<Unit> _enemiesBelowHalfHealthFired = new HashSet<Unit>();

        // Priority queue implemented as a sorted list of pending requests.
        private readonly List<PendingDialogue> _queue = new List<PendingDialogue>();
        private bool _isPlaying = false;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()  => SubscribeToEvents();
        private void OnDisable() => UnsubscribeFromEvents();

        // ── Event subscriptions ───────────────────────────────────────────────

        private void SubscribeToEvents()
        {
            UnitManager.OnUnitDied    += HandleUnitDied;
            UnitManager.OnUnitDamaged += HandleUnitDamaged;
        }

        private void UnsubscribeFromEvents()
        {
            UnitManager.OnUnitDied    -= HandleUnitDied;
            UnitManager.OnUnitDamaged -= HandleUnitDamaged;
        }

        // ── Static public API — called by external code sites ─────────────────

        /// <summary>
        /// Fire a dialogue trigger from anywhere in the codebase.
        /// Use this for contextual triggers not covered by a global event.
        ///
        /// instigator — the unit that caused the trigger (may be null for global triggers).
        ///              For ExcludeKiller-type triggers this is the unit to exclude from
        ///              the speaker pool.
        /// </summary>
        public static void Trigger(DialogueTrigger trigger, Unit instigator = null)
        {
            if (Instance == null) return;
            Instance.EnqueueTrigger(trigger, instigator);
        }

        // ── Internal trigger handlers ─────────────────────────────────────────

        private void HandleUnitDied(Unit unit)
        {
            // Only react to player-unit deaths for ally-downed triggers.
            if (!(unit is PlayerUnit)) return;

            // OnUnitDied fires BEFORE UnitManager.UnregisterUnit removes the unit from
            // PlayerUnits, so the dying unit is still counted in PlayerUnits.Count here.
            // Subtract 1 to get the true number of survivors.
            int survivingPlayerCount = UnitManager.PlayerUnits.Count - 1;

            if (survivingPlayerCount <= 0)
            {
                // No survivors — no one left to speak, do nothing.
                return;
            }

            if (survivingPlayerCount == 1)
            {
                // Exactly one unit left — they are now the last alive.
                EnqueueTrigger(DialogueTrigger.LastAllyAlive, instigator: null);
                return;
            }

            // Multiple units still alive.
            if (!_firstAllyDownedThisCombat)
            {
                _firstAllyDownedThisCombat = true;
                EnqueueTrigger(DialogueTrigger.FirstAllyDowned, instigator: null, excludedUnit: unit);
            }
            else
            {
                // Try a combo first; fall back to normal line if no combo or lost the 50/50.
                bool comboHandled = TryEnqueueCombo(excludedUnit: unit);
                if (!comboHandled)
                    EnqueueTrigger(DialogueTrigger.SubsequentAllyDowned, instigator: null, excludedUnit: unit);
            }
        }

        private void HandleUnitDamaged(Unit victim, Unit attacker)
        {
            // Only track enemy units dropping below 50% health.
            if (!(victim is EnemyUnit)) return;
            if (victim.characterData == null) return;
            if (_enemiesBelowHalfHealthFired.Contains(victim)) return;

            float healthPercent = (float)victim.currentHealth / victim.characterData.maxHealth;
            if (healthPercent <= 0.5f)
            {
                _enemiesBelowHalfHealthFired.Add(victim);
                EnqueueTrigger(DialogueTrigger.EnemyBelowHalfHealth, instigator: null);
            }
        }

        // ── Combo dialogue ────────────────────────────────────────────────────

        /// <summary>
        /// Checks the ComboDialogueDatabase for an entry that exactly matches the
        /// current set of surviving player units (excluding the unit that just died,
        /// which is still in PlayerUnits at this point).
        ///
        /// If a match is found, rolls 50/50. On a win, enqueues the full combo
        /// sequence and returns true. On a loss or no match, returns false so the
        /// caller falls back to normal SubsequentAllyDowned dialogue.
        /// </summary>
        private bool TryEnqueueCombo(Unit excludedUnit)
        {
            // Global 0% check — nothing fires.
            if (DialogueChancePercent <= 0f) return false;
            if (comboDatabase == null) return false;

            // Build the list of surviving CharacterData references.
            // Exclude the unit that just died (still in PlayerUnits at this point).
            var aliveCharacters = UnitManager.PlayerUnits
                .Where(u => u != excludedUnit && u.characterData != null)
                .Select(u => u.characterData)
                .ToList();

            ComboDialogueEntry match = comboDatabase.FindMatch(aliveCharacters);
            if (match == null) return false;

            // 50/50 roll — tails means fall back to normal dialogue.
            if (Random.value < 0.5f) return false;

            // Enqueue each line in the combo as a separate PendingDialogue.
            // All share priority 0 so they queue in arrival order and play back-to-back.
            foreach (var comboLine in match.lines)
            {
                if (comboLine.speaker == null || string.IsNullOrEmpty(comboLine.line)) continue;

                // Find the live Unit whose characterData matches the speaker slot.
                Unit speaker = UnitManager.PlayerUnits
                    .FirstOrDefault(u => u.characterData == comboLine.speaker);

                if (speaker == null) continue;

                Color colour = comboLine.speaker.dialogueData != null
                    ? comboLine.speaker.dialogueData.characterColour
                    : Color.white;

                InsertSorted(new PendingDialogue(speaker, comboLine.line, colour, priority: 0));
            }

            if (!_isPlaying && _queue.Count > 0)
                StartCoroutine(DrainQueue());

            return true;
        }

        // ── Queue management ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves a trigger into a (speaker, line) pair, applies the chance roll,
        /// and inserts it into the priority queue.
        /// </summary>
        private void EnqueueTrigger(DialogueTrigger trigger, Unit instigator, Unit excludedUnit = null)
        {
            if (DialogueChancePercent <= 0f) return;

            Unit speaker = ResolveSpeaker(trigger, instigator, excludedUnit);
            if (speaker == null) return;

            CharacterDialogueData data = speaker.characterData?.dialogueData;
            if (data == null) return;

            DialogueEntry entry = data.GetEntry(trigger);
            if (entry == null || entry.lines == null || entry.lines.Length == 0) return;

            bool shouldPlay = entry.isGuaranteed
                ? DialogueChancePercent > 0f
                : Random.Range(0f, 100f) < DialogueChancePercent;

            if (!shouldPlay) return;

            string line = entry.lines[Random.Range(0, entry.lines.Length)];
            if (string.IsNullOrEmpty(line)) return;

            var pending = new PendingDialogue(speaker, line, data.characterColour, entry.priority);
            InsertSorted(pending);

            if (!_isPlaying)
                StartCoroutine(DrainQueue());
        }

        private void InsertSorted(PendingDialogue incoming)
        {
            // Stable sort: lower priority number = higher importance.
            // Equal-priority items are appended after existing equal-priority items
            // so arrival order is preserved within a priority band.
            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue[i].priority > incoming.priority)
                {
                    _queue.Insert(i, incoming);
                    return;
                }
            }
            _queue.Add(incoming);
        }

        private IEnumerator DrainQueue()
        {
            _isPlaying = true;

            while (_queue.Count > 0)
            {
                var next = _queue[0];
                _queue.RemoveAt(0);

                if (bubbleUI != null)
                    yield return StartCoroutine(bubbleUI.ShowLine(next.speaker, next.line, next.colour));
            }

            _isPlaying = false;
        }

        // ── Speaker resolution ────────────────────────────────────────────────

        private Unit ResolveSpeaker(DialogueTrigger trigger, Unit instigator, Unit excludedUnit)
        {
            // Cast to Unit here so every branch works with List<Unit> and PickRandom's
            // signature stays simple. UnitManager.PlayerUnits is IReadOnlyList<PlayerUnit>.
            var alivePlayers = UnitManager.PlayerUnits.Select(p => (Unit)p).ToList();

            switch (trigger)
            {
                case DialogueTrigger.AllyDownsEnemy:
                {
                    // Instigator is the killer — they cannot speak; bystanders react.
                    var candidates = alivePlayers
                        .Where(u => u != instigator && u != excludedUnit)
                        .ToList();
                    return PickRandom(candidates);
                }

                case DialogueTrigger.LastAllyAlive:
                {
                    // Exactly one player unit should be alive; just pick the first (and only) one.
                    return alivePlayers.Count == 1 ? alivePlayers[0] : PickRandom(alivePlayers);
                }

                default:
                {
                    // Global trigger — any surviving player unit can speak, excluding the dead one.
                    var candidates = alivePlayers.Where(u => u != excludedUnit).ToList();
                    return PickRandom(candidates);
                }
            }
        }

        private static Unit PickRandom(List<Unit> pool)
        {
            if (pool == null || pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        // ── Internal data type ────────────────────────────────────────────────

        private class PendingDialogue
        {
            public readonly Unit   speaker;
            public readonly string line;
            public readonly Color  colour;
            public readonly int    priority;

            public PendingDialogue(Unit speaker, string line, Color colour, int priority)
            {
                this.speaker  = speaker;
                this.line     = line;
                this.colour   = colour;
                this.priority = priority;
            }
        }
    }
}