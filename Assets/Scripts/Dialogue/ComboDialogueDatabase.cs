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
    ///   • A set of required survivors (by CharacterData reference) and a toggle
    ///     controlling whether that set must match exactly (no extra survivors allowed)
    ///     or as a subset (listed characters must be alive; others may also be present).
    ///     Leave the list empty to match any survivor composition.
    ///   • An optional set of required downed characters. All listed characters must
    ///     have died (across the whole combat) for this combo to be eligible.
    ///     Leave empty to match regardless of who has been downed.
    ///   • An ordered list of ComboLines — each line names its speaker (CharacterData)
    ///     and the text to display. There is no limit on the number of lines per entry.
    ///
    /// At runtime, when FirstAllyDowned or SubsequentAllyDowned fires, DialogueManager
    /// collects ALL entries that match the current survivors and downed characters, then
    /// picks one at random with equal probability. If at least one match is found there
    /// is a 50/50 chance a combo plays instead of the normal random line. If no match
    /// is found, the normal line always plays.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Dialogue > Combo Dialogue Database
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Combo Dialogue Database")]
    public class ComboDialogueDatabase : ScriptableObject
    {
        public List<ComboDialogueEntry> entries = new List<ComboDialogueEntry>();

        /// <summary>
        /// Returns all entries whose required survivors and required downed characters
        /// both match the supplied sets, or an empty list if none match.
        ///
        /// aliveCharacters  — CharacterData of units still alive (excluding the unit
        ///                    that just died, which has not yet been unregistered).
        /// downedCharacters — CharacterData of every player unit that has died this
        ///                    combat, including the one that just triggered this check.
        /// </summary>
        public List<ComboDialogueEntry> FindMatches(List<CharacterData> aliveCharacters,
                                                    List<CharacterData> downedCharacters)
        {
            var matches = new List<ComboDialogueEntry>();
            if (aliveCharacters == null) return matches;

            foreach (var entry in entries)
            {
                if (entry.IsMatch(aliveCharacters, downedCharacters))
                    matches.Add(entry);
            }

            return matches;
        }
    }

    /// <summary>
    /// One context-sensitive dialogue sequence. Supports any number of lines.
    ///
    /// Triggers when:
    ///   • requiredSurvivors matches the current alive units — either exactly
    ///     (exactSurvivorsMatch = true) or as a subset (exactSurvivorsMatch = false).
    ///   • Every character in requiredDowned has been downed this combat.
    /// Either list may be left empty to skip that condition entirely.
    /// </summary>
    [Serializable]
    public class ComboDialogueEntry
    {
        [Tooltip("Characters that must be alive when this combo triggers.\n\n" +
                 "exactSurvivorsMatch ON  (default) — alive units must match this list " +
                 "exactly; no one else can be alive. Use for 'last two standing' scenarios.\n\n" +
                 "exactSurvivorsMatch OFF — all listed characters must be alive, but " +
                 "additional survivors are allowed. Use for 'these two are present' scenarios.\n\n" +
                 "Leave empty to match any survivor composition.")]
        public List<CharacterData> requiredSurvivors = new List<CharacterData>();

        [Tooltip("When ON (default), the alive units must match requiredSurvivors exactly — " +
                 "no extra survivors allowed. Use for 'last two standing' combos.\n\n" +
                 "When OFF, requiredSurvivors is treated as a subset check — all listed " +
                 "characters must be alive but others may also be present. Use for " +
                 "'these characters are present' combos that can fire mid-combat.")]
        public bool exactSurvivorsMatch = true;

        [Tooltip("All characters in this list must have been downed this combat for the " +
                 "combo to trigger. For a 'specific unit downed' combo this will typically " +
                 "be a single entry. Leave empty to trigger regardless of who has been downed.")]
        public List<CharacterData> requiredDowned = new List<CharacterData>();

        [Tooltip("Ordered sequence of lines. Each line specifies the speaker and text. " +
                 "There is no limit on how many lines a combo can have.")]
        public List<ComboLine> lines = new List<ComboLine>();

        /// <summary>
        /// Returns true when:
        ///   • requiredSurvivors is empty, OR aliveCharacters satisfies the survivors
        ///     condition (exact match when exactSurvivorsMatch is true; subset when false).
        ///   • requiredDowned is empty, OR every entry in requiredDowned is present in
        ///     downedCharacters.
        /// </summary>
        public bool IsMatch(List<CharacterData> aliveCharacters,
                            List<CharacterData> downedCharacters)
        {
            // Check survivors.
            if (requiredSurvivors.Count > 0)
            {
                if (exactSurvivorsMatch)
                {
                    // Exact match — alive set must equal requiredSurvivors exactly.
                    if (aliveCharacters.Count != requiredSurvivors.Count) return false;
                    foreach (var required in requiredSurvivors)
                    {
                        if (!aliveCharacters.Contains(required)) return false;
                    }
                }
                else
                {
                    // Subset match — all required survivors must be alive; others are allowed.
                    foreach (var required in requiredSurvivors)
                    {
                        if (!aliveCharacters.Contains(required)) return false;
                    }
                }
            }

            // Check downed — every required downed character must be present.
            if (requiredDowned.Count > 0)
            {
                if (downedCharacters == null) return false;
                foreach (var required in requiredDowned)
                {
                    if (!downedCharacters.Contains(required)) return false;
                }
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