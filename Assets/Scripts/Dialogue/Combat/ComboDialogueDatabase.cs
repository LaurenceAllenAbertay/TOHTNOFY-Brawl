using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Combo Dialogue Database")]
    public class ComboDialogueDatabase : ScriptableObject
    {
        public List<ComboDialogueEntry> entries = new List<ComboDialogueEntry>();
        
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
    
    [Serializable]
    public class ComboDialogueEntry
    {
        public List<CharacterData> requiredSurvivors = new List<CharacterData>();
        
        public bool exactSurvivorsMatch = true;
        
        public List<CharacterData> requiredDowned = new List<CharacterData>();

        public List<ComboLine> lines = new List<ComboLine>();
        
        public bool IsMatch(List<CharacterData> aliveCharacters,
                            List<CharacterData> downedCharacters)
        {
            if (requiredSurvivors.Count > 0)
            {
                if (exactSurvivorsMatch)
                {
                    if (aliveCharacters.Count != requiredSurvivors.Count) return false;
                    foreach (var required in requiredSurvivors)
                    {
                        if (!aliveCharacters.Contains(required)) return false;
                    }
                }
                else
                {
                    foreach (var required in requiredSurvivors)
                    {
                        if (!aliveCharacters.Contains(required)) return false;
                    }
                }
            }
            
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
    
    [Serializable]
    public class ComboLine
    {
        public CharacterData speaker;

        [TextArea(1, 3)]
        public string line;
    }
}