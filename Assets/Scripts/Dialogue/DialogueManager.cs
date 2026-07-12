using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class DialogueManager : MonoBehaviour
    {
        public static DialogueManager Instance { get; private set; }

        [Header("Settings")]
        [Range(0f, 100f)]
        public float DialogueChancePercent = 50f;

        [Header("References")]
        [SerializeField] private DialogueBubbleUI bubbleUI;

        [SerializeField] private ComboDialogueDatabase comboDatabase;
        
        private bool _firstAllyDownedThisCombat = false;
        
        private readonly List<CharacterData> _downedCharactersThisCombat = new List<CharacterData>();
        
        private readonly HashSet<Unit> _enemiesBelowHalfHealthFired = new HashSet<Unit>();
        
        private readonly List<PendingDialogue> _queue = new List<PendingDialogue>();
        private bool _isPlaying = false;
        
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

        public static void Trigger(DialogueTrigger trigger, Unit instigator = null)
        {
            if (Instance == null) return;
            Instance.EnqueueTrigger(trigger, instigator);
        }
        
        private void HandleUnitDied(Unit unit)
        {
            if (!(unit is PlayerUnit)) return;
            
            if (unit.characterData != null)
                _downedCharactersThisCombat.Add(unit.characterData);
            
            int survivingPlayerCount = UnitManager.PlayerUnits.Count - 1;

            if (survivingPlayerCount <= 0)
            {
                return;
            }

            if (survivingPlayerCount == 1)
            {
                EnqueueTrigger(DialogueTrigger.LastAllyAlive, instigator: null, excludedUnit: unit);
                return;
            }
            
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
            if (!(victim is EnemyUnit)) return;
            if (victim.maxHealth <= 0) return;
            if (_enemiesBelowHalfHealthFired.Contains(victim)) return;

            float healthPercent = (float)victim.currentHealth / victim.maxHealth;
            if (healthPercent <= 0.5f)
            {
                _enemiesBelowHalfHealthFired.Add(victim);
                EnqueueTrigger(DialogueTrigger.EnemyBelowHalfHealth, instigator: null);
            }
        }

        private bool TryEnqueueCombo(Unit excludedUnit)
        {
            if (DialogueChancePercent <= 0f) return false;
            if (comboDatabase == null) return false;

            var aliveCharacters = UnitManager.PlayerUnits
                .Where(u => u != excludedUnit && u.characterData != null)
                .Select(u => u.characterData)
                .ToList();

            var matches = comboDatabase.FindMatches(aliveCharacters, _downedCharactersThisCombat);
            if (matches.Count == 0) return false;

            if (Random.value < 0.5f) return false;

            ComboDialogueEntry chosen = matches[Random.Range(0, matches.Count)];

            foreach (var comboLine in chosen.lines)
            {
                if (comboLine.speaker == null || string.IsNullOrEmpty(comboLine.line)) continue;

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
        
        private Unit ResolveSpeaker(DialogueTrigger trigger, Unit instigator, Unit excludedUnit)
        {
            var alivePlayers = UnitManager.PlayerUnits.Select(p => (Unit)p).ToList();

            switch (trigger)
            {
                case DialogueTrigger.AllyDownsEnemy:
                {
                    var candidates = alivePlayers
                        .Where(u => u != instigator && u != excludedUnit)
                        .ToList();
                    return PickRandom(candidates);
                }

                case DialogueTrigger.LastAllyAlive:
                {
                    var candidates = alivePlayers.Where(u => u != excludedUnit).ToList();
                    return candidates.Count == 1 ? candidates[0] : PickRandom(candidates);
                }

                default:
                {
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