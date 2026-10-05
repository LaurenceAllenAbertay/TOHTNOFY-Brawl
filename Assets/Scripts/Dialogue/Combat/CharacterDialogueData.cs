using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Character Dialogue Data")]
    public class CharacterDialogueData : ScriptableObject
    {
        [Header("Identity")]
        public Color characterColour = Color.white;

        [Header("Dialogue Entries")]
        public List<DialogueEntry> entries = new List<DialogueEntry>();
        
        public string GetRandomLine(DialogueTrigger trigger)
        {
            var entry = entries.Find(e => e.trigger == trigger);
            if (entry == null || entry.lines == null || entry.lines.Length == 0)
                return null;

            return entry.lines[UnityEngine.Random.Range(0, entry.lines.Length)];
        }

        public DialogueEntry GetEntry(DialogueTrigger trigger)
        {
            return entries.Find(e => e.trigger == trigger);
        }
    }
    
    [Serializable]
    public class DialogueEntry
    {
        public DialogueTrigger trigger;
        
        public int priority = 0;
        
        public bool isGuaranteed = false;

        [TextArea(1, 3)]
        public string[] lines = new string[0];
    }
}