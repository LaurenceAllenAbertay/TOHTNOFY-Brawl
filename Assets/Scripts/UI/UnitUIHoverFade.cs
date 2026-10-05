using UnityEngine;
using UnityEngine.EventSystems;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(CanvasGroup))]
    public class UnitUIHoverFade : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Fade Values")]
        [Range(0f, 1f)]
        [SerializeField] private float hoveredAlpha = 0.15f;

        [SerializeField] private float fadeSpeed = 10f;

        private CanvasGroup canvasGroup;
        private float targetAlpha = 1f;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnEnable()
        {
            targetAlpha = 1f;
            canvasGroup.alpha = 1f;
        }

        private void OnDisable()
        {
            targetAlpha = 1f;
            canvasGroup.alpha = 1f;
        }

        private void Update()
        {
            if (Mathf.Approximately(canvasGroup.alpha, targetAlpha)) return;
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, targetAlpha, fadeSpeed * Time.unscaledDeltaTime);
        }

        public void OnPointerEnter(PointerEventData eventData) => targetAlpha = hoveredAlpha;

        public void OnPointerExit(PointerEventData eventData) => targetAlpha = 1f;
    }
}