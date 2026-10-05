using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Conversation")]
    public class DialogueConversation : ScriptableObject
    {
        public List<DialogueLine> lines = new List<DialogueLine>();

        public bool HasContent
        {
            get
            {
                if (lines == null) return false;

                foreach (var line in lines)
                {
                    if (line != null && !string.IsNullOrEmpty(line.text))
                        return true;
                }

                return false;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (lines == null) return;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;

                if (string.IsNullOrWhiteSpace(line.text))
                {
                    Debug.LogWarning($"[{name}] Line {i} has no text and will be skipped at runtime.", this);
                }

                if (line.role == DialogueSpeakerRole.Explicit && line.speaker == null)
                {
                    Debug.LogWarning($"[{name}] Line {i} has no speaker assigned. " +
                                     $"Set Role to Party Leader, or assign a speaker asset.", this);
                }
            }
        }
#endif
    }

    [Serializable]
    public class DialogueLine
    {
        public DialogueSpeakerRole role = DialogueSpeakerRole.Explicit;

        public DialogueSpeakerData speaker;

        [TextArea(2, 4)]
        public string text;
    }}