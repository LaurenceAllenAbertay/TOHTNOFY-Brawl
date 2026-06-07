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
    /// • Subscribes to existing global events (UnitManager, TurnManager) and converts
    ///   them into DialogueTrigger evaluations automatically.
    /// • Exposes a static Trigger() method for code sites that need to fire a trigger
    ///   that isn't covered by an existing global event.
    /// • Selects a speaker from the correct pool (global random / specific unit /
    ///   exclude-instigator).
    /// • Applies the DialogueChancePercent roll.
    /// • Maintains a priority queue so the most important line always plays first;
    ///   equal-priority lines are played in the order they arrived.
    /// • Hands the final (speaker, line, colour) tuple to DialogueBubbleUI for display.
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
                // Fire LastAllyAlive (the surviving unit is the speaker).
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
                EnqueueTrigger(DialogueTrigger.SubsequentAllyDowned, instigator: null, excludedUnit: unit);
            }
        }

        private void HandleUnitDamaged(Unit victim, Unit attacker)
        {
            // Only track enemy units dropping below 50 % health.
            if (!(victim is EnemyUnit)) return;
            if (victim.characterData == null) return;
            if (_enemiesBelowHalfHealthFired.Contains(victim)) return;

            float healthPercent = (float)victim.currentHealth / victim.characterData.maxHealth;
            if (healthPercent <= 0.5f)
            {
                _enemiesBelowHalfHealthFired.Add(victim);
                // Global trigger — any surviving player unit can comment.
                EnqueueTrigger(DialogueTrigger.EnemyBelowHalfHealth, instigator: null);
            }
        }

        // ── Queue management ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves a trigger into a (speaker, line) pair, applies the chance roll,
        /// and inserts it into the priority queue.
        ///
        /// excludedUnit — unit that must not be chosen as speaker (e.g. the unit that
        ///                just died, or the kill instigator for bystander-reaction triggers).
        /// </summary>
        private void EnqueueTrigger(DialogueTrigger trigger, Unit instigator, Unit excludedUnit = null)
        {
            // 0% — nothing ever plays.
            if (DialogueChancePercent <= 0f) return;

            // Build the candidate speaker pool based on trigger type.
            Unit speaker = ResolveSpeaker(trigger, instigator, excludedUnit);
            if (speaker == null) return;

            // Retrieve this speaker's dialogue data.
            CharacterDialogueData data = speaker.characterData?.dialogueData;
            if (data == null) return;

            DialogueEntry entry = data.GetEntry(trigger);
            if (entry == null || entry.lines == null || entry.lines.Length == 0) return;

            // Chance roll.
            bool shouldPlay = entry.isGuaranteed
                ? DialogueChancePercent > 0f          // Guaranteed fires unless 0%
                : Random.Range(0f, 100f) < DialogueChancePercent;

            if (!shouldPlay) return;

            string line = entry.lines[Random.Range(0, entry.lines.Length)];
            if (string.IsNullOrEmpty(line)) return;

            // Insert into the priority queue (stable sort: lower priority int = higher priority).
            var pending = new PendingDialogue(speaker, line, data.characterColour, entry.priority);
            InsertSorted(pending);

            if (!_isPlaying)
                StartCoroutine(DrainQueue());
        }

        private void InsertSorted(PendingDialogue incoming)
        {
            // Find the first item with a strictly higher priority number (i.e. lower importance)
            // and insert before it, preserving arrival order for equal-priority items.
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

        /// <summary>
        /// Returns the unit that should deliver this line.
        ///
        /// Trigger-type rules:
        ///   AllyDownsEnemy  → bystander reaction: random player unit EXCLUDING instigator
        ///                     and excludedUnit (the downed unit, if supplied).
        ///   LastAllyAlive   → specifically the sole surviving player unit.
        ///   All others      → random surviving player unit, excluding excludedUnit.
        /// </summary>
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
                    // Global trigger — any surviving player unit can speak.
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