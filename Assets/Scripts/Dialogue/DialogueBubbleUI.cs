using System.Collections;
using TMPro;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Displays a single speech bubble for the dialogue system.
    ///
    /// Required Unity hierarchy
    /// ────────────────────────
    ///   DialogueBubble  ← this script only. Anchor: middle-centre, Pivot: 0.5,0.5.
    ///                      No ContentSizeFitter here.
    ///     └── Text      ← TextMeshProUGUI + ContentSizeFitter (both axes: Preferred Size)
    ///                      Anchor: middle-centre, Pivot: 0.5,0.5.
    ///                      Set a fixed Width (e.g. 300) in the RectTransform.
    ///                      Height is driven by the ContentSizeFitter.
    ///
    /// The bubble root is repositioned each frame. The Text child auto-sizes its
    /// height to fit the content via ContentSizeFitter. The script reads the Text
    /// child's rect for edge-clamping so the full bubble always stays on screen.
    ///
    /// Display duration
    /// ────────────────
    ///   Typewriter phase: typewriterDuration seconds (default 1 s). Set to 0 for instant.
    ///   Hold phase:       baseDuration + line.Length * perCharacterDuration  (defaults: 5 s + 0.01 s/char)
    /// </summary>
    public class DialogueBubbleUI : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("References")]
        [Tooltip("TextMeshProUGUI on the Text child.")]
        [SerializeField] private TextMeshProUGUI dialogueText;

        [Tooltip("RectTransform of the bubble root (usually this GameObject).")]
        [SerializeField] private RectTransform bubbleRect;

        [Tooltip("RectTransform of the Text child — used for size measurement when clamping.")]
        [SerializeField] private RectTransform textRect;

        [Tooltip("Camera used for world-to-screen projection. Falls back to Camera.main.")]
        [SerializeField] private Camera targetCamera;

        [Header("Timing")]
        [Tooltip("Total seconds to type out the full line. Set to 0 to show it instantly.")]
        [SerializeField] private float typewriterDuration = 1f;

        [Tooltip("Seconds the fully-typed line stays visible before the bubble hides.")]
        [SerializeField] private float baseDuration = 5f;

        [SerializeField] private float perCharacterDuration = 0.01f;

        [Header("Screen Edge")]
        [Tooltip("Pixel gap between the bubble edge and the screen border.")]
        [SerializeField] private float edgePadding = 16f;

        [Header("On-screen Offset")]
        [Tooltip("World-space units to raise the anchor above the unit's pivot.")]
        [SerializeField] private float worldHeightOffset = 1.5f;

        // ── Private state ─────────────────────────────────────────────────────

        private Unit   _currentSpeaker;
        private Canvas _parentCanvas;
        private bool   _visible;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            _parentCanvas = GetComponentInParent<Canvas>();

            if (bubbleRect == null)
                bubbleRect = GetComponent<RectTransform>();

            // Fall back to bubbleRect if no separate textRect assigned.
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

        // ── Public API ────────────────────────────────────────────────────────

        public IEnumerator ShowLine(Unit speaker, string line, Color colour)
        {
            if (speaker == null || string.IsNullOrEmpty(line)) yield break;

            _currentSpeaker = speaker;

            if (dialogueText != null)
            {
                // Set the full string upfront so ContentSizeFitter can measure the
                // final bubble height before we start revealing characters.
                dialogueText.text             = line;
                dialogueText.color            = colour;
                dialogueText.maxVisibleCharacters = 0;
            }

            // Show first so the Canvas layout pass can run this frame.
            SetVisible(true);

            // Yield one frame so ContentSizeFitter on the Text child resizes itself
            // to fit the complete text before we read textRect.rect for clamping.
            yield return null;

            UpdateBubblePosition(speaker);

            // ── Typewriter reveal ─────────────────────────────────────────────
            if (dialogueText != null)
            {
                int charCount = line.Length;

                if (typewriterDuration <= 0f || charCount == 0)
                {
                    // Instant reveal — skip the animation entirely.
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

                    // Guarantee all characters are visible at the end.
                    dialogueText.maxVisibleCharacters = charCount;
                }
            }

            // ── Hold the fully-typed line ─────────────────────────────────────
            float holdDuration = baseDuration + line.Length * perCharacterDuration;
            yield return new WaitForSeconds(holdDuration);

            SetVisible(false);
            _currentSpeaker = null;
        }

        // ── Positioning ───────────────────────────────────────────────────────

        private void UpdateBubblePosition(Unit speaker)
        {
            if (targetCamera == null || bubbleRect == null || _parentCanvas == null || speaker == null)
                return;

            // Project world position to screen space.
            Vector3 worldPos = speaker.transform.position + Vector3.up * worldHeightOffset;
            Vector3 viewport = targetCamera.WorldToViewportPoint(worldPos);

            // When the unit is behind the camera the viewport coords are mirrored —
            // flip them so the bubble still appears on the correct side of the screen.
            if (viewport.z < 0f)
            {
                viewport.x = 1f - viewport.x;
                viewport.y = 1f - viewport.y;
            }

            Vector2 screenPos = new Vector2(viewport.x * Screen.width, viewport.y * Screen.height);

            // Convert to canvas local space.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentCanvas.transform as RectTransform,
                screenPos,
                _parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : targetCamera,
                out Vector2 localPos);

            // Clamp using the Text child's actual rendered size so the full text
            // is always on screen. textRect.rect is accurate after ContentSizeFitter
            // has run (guaranteed by the yield return null in ShowLine).
            RectTransform canvasRect = _parentCanvas.transform as RectTransform;
            Vector2 canvasHalf = canvasRect.rect.size * 0.5f;

            float halfW = (textRect.rect.width  * Mathf.Abs(textRect.localScale.x)) * 0.5f + edgePadding;
            float halfH = (textRect.rect.height * Mathf.Abs(textRect.localScale.y)) * 0.5f + edgePadding;

            localPos.x = Mathf.Clamp(localPos.x, -canvasHalf.x + halfW,  canvasHalf.x - halfW);
            localPos.y = Mathf.Clamp(localPos.y, -canvasHalf.y + halfH,  canvasHalf.y - halfH);

            bubbleRect.anchoredPosition = localPos;
        }

        // ── Visibility ────────────────────────────────────────────────────────

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