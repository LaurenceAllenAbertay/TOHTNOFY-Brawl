using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class OverworldDialogueUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameObject panelRoot;

        [SerializeField] private TextMeshProUGUI nameText;

        [SerializeField] private TextMeshProUGUI dialogueText;

        [SerializeField] private Image portraitImage;

        [SerializeField] private GameObject continueIndicator;

        [Header("Typewriter")]
        [SerializeField] private float charactersPerSecond = 45f;

        public bool IsTyping { get; private set; }

        private bool _skipRequested;

        private void Awake()
        {
            if (panelRoot == null)
                panelRoot = gameObject;

            Hide();
        }

        public void Show()
        {
            panelRoot.SetActive(true);

            if (continueIndicator != null)
                continueIndicator.SetActive(false);
        }

        public void Hide()
        {
            IsTyping = false;
            panelRoot.SetActive(false);
        }

        public void CompleteLine()
        {
            _skipRequested = true;
        }

        public IEnumerator PlayLine(DialogueSpeakerData speaker, string text)
        {
            ApplySpeaker(speaker);

            if (continueIndicator != null)
                continueIndicator.SetActive(false);

            dialogueText.text = text;
            dialogueText.ForceMeshUpdate();

            int totalCharacters = dialogueText.textInfo.characterCount;
            dialogueText.maxVisibleCharacters = 0;

            IsTyping = true;
            _skipRequested = false;

            if (charactersPerSecond > 0f && totalCharacters > 0)
            {
                float elapsed = 0f;
                int revealed = 0;

                while (!_skipRequested && revealed < totalCharacters)
                {
                    elapsed += Time.deltaTime;
                    revealed = Mathf.Clamp(Mathf.FloorToInt(elapsed * charactersPerSecond), 0, totalCharacters);
                    dialogueText.maxVisibleCharacters = revealed;
                    yield return null;
                }
            }

            dialogueText.maxVisibleCharacters = totalCharacters;

            IsTyping = false;
            _skipRequested = false;

            if (continueIndicator != null)
                continueIndicator.SetActive(true);
        }

        private void ApplySpeaker(DialogueSpeakerData speaker)
        {
            if (nameText != null)
            {
                bool hasName = speaker != null && !string.IsNullOrEmpty(speaker.DisplayName);
                nameText.gameObject.SetActive(hasName);

                if (hasName)
                {
                    nameText.text = speaker.DisplayName;
                    nameText.color = speaker.SpeakerColour;
                }
            }

            if (portraitImage != null)
            {
                Sprite portrait = speaker != null ? speaker.SpeakerPortrait : null;
                portraitImage.gameObject.SetActive(portrait != null);

                if (portrait != null)
                    portraitImage.sprite = portrait;
            }
        }
    }
}