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
    /// • For FirstAllyDowned and SubsequentAllyDowned: checks the ComboDialogueDatabase
    ///   for entries that match both the current survivors and the downed characters this
    ///   combat. If any match, one is chosen at random with equal probability and there is
    ///   a 50/50 chance it plays instead of the normal random line. If no match is found,
    ///   the normal line always plays.
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
                 "Checked on FirstAllyDowned and SubsequentAllyDowned. All entries that match " +
                 "the current survivors and downed characters are collected; one is picked at " +
                 "random with equal probability. There is then a 50/50 chance the combo plays " +
                 "instead of the normal random line.")]
        [SerializeField] private ComboDialogueDatabase comboDatabase;

        // ── Internal state ────────────────────────────────────────────────────

        // Tracks whether the very first player-unit death has occurred this combat.
        private bool _firstAllyDownedThisCombat = false;

        // Accumulates the CharacterData of every player unit downed this combat.
        // Used by TryEnqueueCombo to match entries that require a specific downed character.
        private readonly List<CharacterData> _downedCharactersThisCombat = new List<CharacterData>();

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

            // Record this character as downed for combo matching.
            // Done before the combo check so the dying unit's CharacterData is
            // included in the downed list when IsMatch evaluates this death.
            if (unit.characterData != null)
                _downedCharactersThisCombat.Add(unit.characterData);

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
                // Pass the dying unit as excludedUnit: OnUnitDied fires before UnregisterUnit,
                // so the dead unit is still in PlayerUnits and must be explicitly excluded here.
                EnqueueTrigger(DialogueTrigger.LastAllyAlive, instigator: null, excludedUnit: unit);
                return;
            }

            // Multiple units still alive. Try a combo first on every death (first or
            // subsequent) — combos can now match on first-death scenarios too.
            bool comboHandled = TryEnqueueCombo(excludedUnit: unit);
            if (!comboHandled)
            {
                if (!_firstAllyDownedThisCombat)
                    EnqueueTrigger(DialogueTrigger.FirstAllyDowned, instigator: null, excludedUnit: unit);
                else
                    EnqueueTrigger(DialogueTrigger.SubsequentAllyDowned, instigator: null, excludedUnit: unit);
            }

            _firstAllyDownedThisCombat = true;
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
        /// Checks the ComboDialogueDatabase for all entries that match the current
        /// survivors and downed characters, then picks one at random with equal
        /// probability. Rolls 50/50 on whether a combo plays at all.
        ///
        /// Returns true if a combo was enqueued (caller should not also fire the
        /// normal FirstAllyDowned / SubsequentAllyDowned line).
        /// Returns false if no matches exist or the 50/50 roll failed, so the caller
        /// falls back to normal dialogue.
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

            var matches = comboDatabase.FindMatches(aliveCharacters, _downedCharactersThisCombat);
            if (matches.Count == 0) return false;

            // 50/50 roll — tails means fall back to normal dialogue.
            if (Random.value < 0.5f) return false;

            // Pick one matching entry at random with equal probability.
            ComboDialogueEntry chosen = matches[Random.Range(0, matches.Count)];

            // Enqueue each line in the combo as a separate PendingDialogue.
            // All share priority 0 so they queue in arrival order and play back-to-back.
            foreach (var comboLine in chosen.lines)
            {
                if (comboLine.speaker == null || string.IsNullOrEmpty(comboLine.line)) continue;

                // Find the live Unit whose characterData matches the speaker slot.
                // A combo speaker may be the downed unit (e.g. single-speaker "directed at"
                // combos) — search all PlayerUnits including the still-registered dying unit.
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
                    // The dying unit is still in PlayerUnits when this fires (unregistration
                    // happens after OnUnitDied). Exclude it so only the true survivor speaks.
                    var candidates = alivePlayers.Where(u => u != excludedUnit).ToList();
                    return candidates.Count == 1 ? candidates[0] : PickRandom(candidates);
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