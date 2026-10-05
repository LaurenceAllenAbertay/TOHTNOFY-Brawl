using System.Collections;
using TMPro;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class DialogueBubbleUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private TextMeshProUGUI dialogueText;

        [SerializeField] private RectTransform bubbleRect;

        [SerializeField] private RectTransform textRect;

        [SerializeField] private Camera targetCamera;

        [Header("Timing")]
        [SerializeField] private float typewriterDuration = 1f;

        [SerializeField] private float baseDuration = 5f;

        [SerializeField] private float perCharacterDuration = 0.01f;

        [Header("Screen Edge")]
        [SerializeField] private float edgePadding = 16f;

        [Header("On-screen Offset")]
        [SerializeField] private float worldHeightOffset = 1.5f;
        
        public static event System.Action<Unit> OnDialogueStarted;

        public static event System.Action<Unit> OnDialogueEnded;
        
        private Unit   _currentSpeaker;
        private Canvas _parentCanvas;
        private bool   _visible;
        
        private void Awake()
        {
            _parentCanvas = GetComponentInParent<Canvas>();

            if (bubbleRect == null)
                bubbleRect = GetComponent<RectTransform>();
            
            if (textRect == null)
                textRect = bubbleRect;

            if (targetCamera == null)
                targetCamera = Camera.main;

            SetVisible(false);
        }

        private void LateUpdate()
        {
            if (_visible && _currentSpeaker != null)
                UpdateBubblePosition(_currentSpeaker);
        }
        
        public IEnumerator ShowLine(Unit speaker, string line, Color colour)
        {
            if (speaker == null || string.IsNullOrEmpty(line)) yield break;

            _currentSpeaker = speaker;

            if (dialogueText != null)
            {
                dialogueText.text             = line;
                dialogueText.color            = colour;
                dialogueText.maxVisibleCharacters = 0;
            }
            
            OnDialogueStarted?.Invoke(speaker);
            
            SetVisible(true);

            yield return null;

            UpdateBubblePosition(speaker);

            if (dialogueText != null)
            {
                int charCount = line.Length;

                if (typewriterDuration <= 0f || charCount == 0)
                {
                    dialogueText.maxVisibleCharacters = charCount;
                }
                else
                {
                    float elapsed       = 0f;
                    float perCharDelay  = typewriterDuration / charCount;

                    while (elapsed < typewriterDuration)
                    {
                        elapsed += Time.deltaTime;
                        int charsToShow = Mathf.Clamp(Mathf.FloorToInt(elapsed / perCharDelay), 0, charCount);
                        dialogueText.maxVisibleCharacters = charsToShow;
                        yield return null;
                    }
                    
                    dialogueText.maxVisibleCharacters = charCount;
                }
            }
            
            float holdDuration = baseDuration + line.Length * perCharacterDuration;
            yield return new WaitForSeconds(holdDuration);

            SetVisible(false);
            OnDialogueEnded?.Invoke(speaker);
            _currentSpeaker = null;
        }

        private void UpdateBubblePosition(Unit speaker)
        {
            if (targetCamera == null || bubbleRect == null || _parentCanvas == null || speaker == null)
                return;
            
            Vector3 worldPos = speaker.transform.position + Vector3.up * worldHeightOffset;
            Vector3 viewport = targetCamera.WorldToViewportPoint(worldPos);
            
            if (viewport.z < 0f)
            {
                viewport.x = 1f - viewport.x;
                viewport.y = 1f - viewport.y;
            }

            Vector2 screenPos = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);
            
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentCanvas.transform as RectTransform,
                screenPos,
                _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCamera,
                out Vector2 localPos);
            
            RectTransform canvasRect = _parentCanvas.transform as RectTransform;
            Vector2 canvasHalf = canvasRect.rect.size * 0.5f;

            float halfW = (textRect.rect.width  * Mathf.Abs(textRect.localScale.x)) * 0.5f + edgePadding;
            float halfH = (textRect.rect.height * Mathf.Abs(textRect.localScale.y)) * 0.5f + edgePadding;

            localPos.x = Mathf.Clamp(localPos.x, -canvasHalf.x + halfW,  canvasHalf.x - halfW);
            localPos.y = Mathf.Clamp(localPos.y, -canvasHalf.y + halfH,  canvasHalf.y - halfH);

            bubbleRect.anchoredPosition = localPos;
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (bubbleRect != null)
                bubbleRect.gameObject.SetActive(visible);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            edgePadding = Mathf.Max(0f, edgePadding);
        }
#endif
    }
}