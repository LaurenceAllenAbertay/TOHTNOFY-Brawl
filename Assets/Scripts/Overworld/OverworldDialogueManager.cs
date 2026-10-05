using System;
using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class OverworldDialogueManager : MonoBehaviour
    {
        public static OverworldDialogueManager Instance { get; private set; }

        public static bool IsPlaying { get; private set; }

        public static event Action OnConversationStarted;

        public static event Action OnConversationEnded;

        [Header("References")]
        [SerializeField] private OverworldDialogueUI dialogueUI;

        private bool _advanceRequested;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            OverworldInputHandler.OnDialogueAdvance += HandleAdvance;
        }

        private void OnDisable()
        {
            OverworldInputHandler.OnDialogueAdvance -= HandleAdvance;

            if (IsPlaying)
            {
                IsPlaying = false;
                OverworldInputHandler.ExitDialogueMode();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public bool TryPlay(DialogueConversation conversation)
        {
            if (IsPlaying) return false;
            if (dialogueUI == null) return false;
            if (conversation == null || !conversation.HasContent) return false;

            StartCoroutine(PlayRoutine(conversation));
            return true;
        }

        private IEnumerator PlayRoutine(DialogueConversation conversation)
        {
            IsPlaying = true;
            _advanceRequested = false;

            OverworldInputHandler.EnterDialogueMode();
            OnConversationStarted?.Invoke();

            dialogueUI.Show();

            foreach (var line in conversation.lines)
            {
                if (line == null || string.IsNullOrEmpty(line.text)) continue;

                _advanceRequested = false;

                yield return StartCoroutine(dialogueUI.PlayLine(ResolveSpeaker(line), line.text));

                while (!_advanceRequested)
                    yield return null;
            }

            dialogueUI.Hide();

            OverworldInputHandler.ExitDialogueMode();

            IsPlaying = false;
            OnConversationEnded?.Invoke();
        }

        private static DialogueSpeakerData ResolveSpeaker(DialogueLine line)
        {
            if (line.role != DialogueSpeakerRole.PartyLeader)
                return line.speaker;

            var party = OverworldPartyManager.Instance;
            if (party == null || party.LeaderCharacter == null)
            {
                Debug.LogWarning("[OverworldDialogueManager] Line is assigned to the party leader " +
                                 "but no leader was found — falling back to the explicit speaker.");
                return line.speaker;
            }

            return party.LeaderCharacter;
        }

        private void HandleAdvance()
        {
            if (!IsPlaying) return;

            if (dialogueUI != null && dialogueUI.IsTyping)
            {
                dialogueUI.CompleteLine();
                return;
            }

            _advanceRequested = true;
        }
    }
}