using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Holds all context-sensitive multi-speaker dialogue sequences.
    /// Assign one instance of this SO to the DialogueManager in the Inspector.
    ///
    /// Each ComboDialogueEntry defines:
    ///   • An exact set of required survivors (by CharacterData reference).
    ///     The combo only triggers when the alive player units match this set exactly.
    ///   • An ordered list of ComboLines — each line names its speaker (CharacterData)
    ///     and the text to display. There is no limit on the number of lines per entry.
    ///
    /// At runtime, when SubsequentAllyDowned fires and exactly two or more
    /// survivors remain, DialogueManager checks this database for a matching entry.
    /// If one is found there is a 50/50 chance the combo plays instead of the
    /// normal random SubsequentAllyDowned line. If no match is found, the normal
    /// line always plays.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Dialogue > Combo Dialogue Database
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Combo Dialogue Database")]
    public class ComboDialogueDatabase : ScriptableObject
    {
        public List<ComboDialogueEntry> entries = new List<ComboDialogueEntry>();

        /// <summary>
        /// Returns the first entry whose required survivors match the supplied set
        /// exactly (same CharacterData references, regardless of order), or null
        /// if no entry matches.
        /// </summary>
        public ComboDialogueEntry FindMatch(List<CharacterData> aliveCharacters)
        {
            if (aliveCharacters == null) return null;

            foreach (var entry in entries)
            {
                if (entry.IsMatch(aliveCharacters))
                    return entry;
            }

            return null;
        }
    }

    /// <summary>
    /// One context-sensitive dialogue sequence. Triggers when the alive player units
    /// match requiredSurvivors exactly. Supports any number of lines.
    /// </summary>
    [Serializable]
    public class ComboDialogueEntry
    {
        [Tooltip("The exact set of CharacterData assets that must be alive for this " +
                 "combo to trigger. Order does not matter. All others must be dead.")]
        public List<CharacterData> requiredSurvivors = new List<CharacterData>();

        [Tooltip("Ordered sequence of lines. Each line specifies the speaker and text. " +
                 "There is no limit on how many lines a combo can have.")]
        public List<ComboLine> lines = new List<ComboLine>();

        /// <summary>
        /// Returns true when aliveCharacters contains exactly the same CharacterData
        /// references as requiredSurvivors, in any order.
        /// </summary>
        public bool IsMatch(List<CharacterData> aliveCharacters)
        {
            if (aliveCharacters.Count != requiredSurvivors.Count) return false;

            foreach (var required in requiredSurvivors)
            {
                if (!aliveCharacters.Contains(required)) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// A single line within a combo sequence — a speaker and what they say.
    /// </summary>
    [Serializable]
    public class ComboLine
    {
        [Tooltip("CharacterData of the unit that delivers this line. " +
                 "Must be one of the requiredSurvivors on the parent entry.")]
        public CharacterData speaker;

        [Tooltip("The line of dialogue spoken.")]
        [TextArea(1, 3)]
        public string line;
    }
}