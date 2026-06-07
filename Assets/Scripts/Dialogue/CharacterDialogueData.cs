using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// One ScriptableObject per playable (or enemy) character.
    /// Assign this on the character's CharacterData SO via the dialogueData field.
    ///
    /// Each entry maps a DialogueTrigger to a pool of lines (equal random chance)
    /// plus a priority and a guaranteed flag.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Dialogue > Character Dialogue Data
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Character Dialogue Data")]
    public class CharacterDialogueData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Colour used to tint this character's speech bubble text.")]
        public Color characterColour = Color.white;

        [Header("Dialogue Entries")]
        public List<DialogueEntry> entries = new List<DialogueEntry>();

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Returns a random line for the given trigger, or null if no entry exists.
        /// </summary>
        public string GetRandomLine(DialogueTrigger trigger)
        {
            var entry = entries.Find(e => e.trigger == trigger);
            if (entry == null || entry.lines == null || entry.lines.Length == 0)
                return null;

            return entry.lines[UnityEngine.Random.Range(0, entry.lines.Length)];
        }

        /// <summary>
        /// Returns the DialogueEntry for the given trigger, or null if none exists.
        /// </summary>
        public DialogueEntry GetEntry(DialogueTrigger trigger)
        {
            return entries.Find(e => e.trigger == trigger);
        }
    }

    /// <summary>
    /// One dialogue entry: a trigger, optional priority, guaranteed flag, and a pool of lines.
    /// </summary>
    [Serializable]
    public class DialogueEntry
    {
        [Tooltip("Which in-combat event triggers this entry.")]
        public DialogueTrigger trigger;

        [Tooltip("Lower number = higher priority. When two triggers fire simultaneously, " +
                 "the lower priority value plays first. Equal priority values are queued " +
                 "in the order they were received.")]
        public int priority = 0;

        [Tooltip("If true, this line always plays (subject to the global 0% override). " +
                 "If false, the global DialogueChancePercent roll decides whether it plays.")]
        public bool isGuaranteed = false;

        [Tooltip("Pool of possible lines. One is chosen at random with equal probability.")]
        [TextArea(1, 3)]
        public string[] lines = new string[0];
    }
}