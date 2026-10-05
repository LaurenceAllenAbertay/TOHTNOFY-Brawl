using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/Conversation Set")]
    public class DialogueConversationSet : ScriptableObject
    {
        [Header("Checked top to bottom, first match wins")]
        public List<ConversationVariant> variants = new List<ConversationVariant>();

        [Header("Used when no variant matches")]
        public DialogueConversation fallback;

        public DialogueConversation Resolve(CharacterData leader, IReadOnlyList<CharacterData> party)
        {
            foreach (var variant in variants)
            {
                if (variant == null) continue;
                if (variant.conversation == null || !variant.conversation.HasContent) continue;
                if (!variant.IsMatch(leader, party)) continue;

                return variant.conversation;
            }

            return fallback != null && fallback.HasContent ? fallback : null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (variants == null) return;

            bool unconditionalSeen = false;

            for (int i = 0; i < variants.Count; i++)
            {
                var variant = variants[i];
                if (variant == null) continue;

                if (variant.conversation == null)
                {
                    Debug.LogWarning($"[{name}] Variant {i} has no conversation assigned.", this);
                    continue;
                }

                if (unconditionalSeen)
                {
                    Debug.LogWarning($"[{name}] Variant {i} can never be reached — " +
                                     $"an earlier variant has no conditions and always matches.", this);
                }

                if (!variant.HasConditions)
                    unconditionalSeen = true;

                ValidateContradictions(i, variant);
                ValidateSpeakers(i, variant);
            }

            if (!unconditionalSeen && (fallback == null || !fallback.HasContent))
            {
                Debug.LogWarning($"[{name}] Every variant has conditions and there is no fallback. " +
                                 $"Some party compositions will get no dialogue at all.", this);
            }
        }

        private void ValidateContradictions(int index, ConversationVariant variant)
        {
            foreach (var excluded in variant.excludedPartyMembers)
            {
                if (excluded == null) continue;

                if (variant.requiredLeader == excluded)
                {
                    Debug.LogWarning($"[{name}] Variant {index} requires '{excluded.characterName}' as leader " +
                                     $"but also excludes them — it can never match.", this);
                }

                foreach (var required in variant.requiredPartyMembers)
                {
                    if (required == excluded)
                    {
                        Debug.LogWarning($"[{name}] Variant {index} both requires and excludes " +
                                         $"'{excluded.characterName}' — it can never match.", this);
                    }
                }
            }
        }

        private void ValidateSpeakers(int index, ConversationVariant variant)
        {
            var lines = variant.conversation.lines;
            if (lines == null) return;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line == null) continue;
                if (line.role != DialogueSpeakerRole.Explicit) continue;

                var character = line.speaker as CharacterData;
                if (character == null) continue;

                if (variant.requiredLeader == character) continue;
                if (variant.requiredPartyMembers.Contains(character)) continue;

                Debug.LogWarning($"[{name}] Variant {index} plays '{variant.conversation.name}' line {i}, " +
                                 $"spoken by '{character.characterName}', but nothing guarantees they are in the party.", this);
            }
        }
#endif
    }

    [Serializable]
    public class ConversationVariant
    {
        public CharacterData requiredLeader;

        public List<CharacterData> requiredPartyMembers = new List<CharacterData>();

        public List<CharacterData> excludedPartyMembers = new List<CharacterData>();

        public DialogueConversation conversation;

        public bool HasConditions
        {
            get
            {
                if (requiredLeader != null) return true;

                foreach (var required in requiredPartyMembers)
                {
                    if (required != null) return true;
                }

                foreach (var excluded in excludedPartyMembers)
                {
                    if (excluded != null) return true;
                }

                return false;
            }
        }

        public bool IsMatch(CharacterData leader, IReadOnlyList<CharacterData> party)
        {
            if (requiredLeader != null && leader != requiredLeader) return false;

            foreach (var required in requiredPartyMembers)
            {
                if (required == null) continue;
                if (!PartyContains(party, required)) return false;
            }

            foreach (var excluded in excludedPartyMembers)
            {
                if (excluded == null) continue;
                if (PartyContains(party, excluded)) return false;
            }

            return true;
        }

        private static bool PartyContains(IReadOnlyList<CharacterData> party, CharacterData character)
        {
            if (party == null) return false;

            for (int i = 0; i < party.Count; i++)
            {
                if (party[i] == character) return true;
            }

            return false;
        }
    }
}