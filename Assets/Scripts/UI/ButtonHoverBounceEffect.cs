using UnityEngine;
using UnityEngine.EventSystems;

namespace DDD.TNFY.BRAWL
{
    public class ButtonHoverBounceEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Target")]
        [SerializeField] private RectTransform visualTarget;

        [Header("Grow On Approach")]
        [SerializeField] private bool enableApproachGrow = true;
        [SerializeField] private float approachRadius = 80f;
        [SerializeField] private float maxApproachScale = 1.15f;
        [SerializeField] private float approachLerpSpeed = 12f;

        [Header("Bounce On Enter/Exit")]
        [SerializeField] private bool enableBounce = true;
        [SerializeField] private float bounceDuration = 0.18f;
        [SerializeField] private float bounceHeight = 0.15f;

        [Header("Pulse While Hovering")]
        [SerializeField] private bool enablePulse = true;
        [SerializeField] private float pulseSpeed = 3f;
        [SerializeField] private float pulseHeight = 0.03f;

        private enum HoverState { Idle, EnteringBounce, Pulsing, ExitingBounce }

        private RectTransform selfRect;
        private Canvas parentCanvas;
        private Camera uiCamera;

        private float currentApproachScale = 1f;
        private HoverState state = HoverState.Idle;
        private float stateTimer;
        private float pulseTimer;

        private Vector3 baseLocalScale;

        void Awake()
        {
            selfRect = GetComponent<RectTransform>();
            if (visualTarget == null)
                visualTarget = selfRect;

            baseLocalScale = visualTarget.localScale;

            parentCanvas = GetComponentInParent<Canvas>();
        }

        void OnEnable()
        {
            currentApproachScale = 1f;
            state = HoverState.Idle;
            stateTimer = 0f;
            pulseTimer = 0f;
            ApplyScale();
        }

        void OnDisable()
        {
            visualTarget.localScale = baseLocalScale;
        }

        void Update()
        {
            UpdateCachedCamera();
            UpdateApproachScale();
            UpdateState();
            ApplyScale();
        }

        private void UpdateCachedCamera()
        {
            if (parentCanvas == null) return;

            uiCamera = parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : parentCanvas.worldCamera;
        }

        private void UpdateApproachScale()
        {
            if (!enableApproachGrow)
            {
                currentApproachScale = 1f;
                return;
            }

            float distance = GetScreenDistanceToButton();
            float targetScale = distance <= approachRadius
                ? Mathf.Lerp(maxApproachScale, 1f, distance / approachRadius)
                : 1f;

            currentApproachScale = Mathf.Lerp(currentApproachScale, targetScale, Time.unscaledDeltaTime * approachLerpSpeed);
        }

        private float GetScreenDistanceToButton()
        {
            Vector2 mousePos = Input.mousePosition;

            Vector3[] worldCorners = new Vector3[4];
            selfRect.GetWorldCorners(worldCorners);

            Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);

            for (int i = 0; i < 4; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, worldCorners[i]);
                min = Vector2.Min(min, screenPoint);
                max = Vector2.Max(max, screenPoint);
            }

            Vector2 nearestPoint = new Vector2(
                Mathf.Clamp(mousePos.x, min.x, max.x),
                Mathf.Clamp(mousePos.y, min.y, max.y));

            return Vector2.Distance(nearestPoint, mousePos);
        }

        private void UpdateState()
        {
            switch (state)
            {
                case HoverState.EnteringBounce:
                    stateTimer += Time.unscaledDeltaTime;
                    if (stateTimer >= bounceDuration)
                    {
                        state = HoverState.Pulsing;
                        pulseTimer = 0f;
                    }
                    break;

                case HoverState.Pulsing:
                    pulseTimer += Time.unscaledDeltaTime * pulseSpeed;
                    break;

                case HoverState.ExitingBounce:
                    stateTimer += Time.unscaledDeltaTime;
                    if (stateTimer >= bounceDuration)
                        state = HoverState.Idle;
                    break;

                case HoverState.Idle:
                default:
                    break;
            }
        }

        private float GetOneShotBounceMultiplier()
        {
            float t = Mathf.Clamp01(stateTimer / bounceDuration);
            return 1f + Mathf.Sin(t * Mathf.PI) * bounceHeight;
        }

        private void ApplyScale()
        {
            float shapeMultiplier = 1f;

            if (enableBounce && (state == HoverState.EnteringBounce || state == HoverState.ExitingBounce))
            {
                shapeMultiplier = GetOneShotBounceMultiplier();
            }
            else if (enablePulse && state == HoverState.Pulsing)
            {
                shapeMultiplier = 1f + (Mathf.Sin(pulseTimer) * 0.5f + 0.5f) * pulseHeight;
            }

            visualTarget.localScale = baseLocalScale * currentApproachScale * shapeMultiplier;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!enableBounce)
            {
                state = HoverState.Pulsing;
                pulseTimer = 0f;
                return;
            }

            state = HoverState.EnteringBounce;
            stateTimer = 0f;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!enableBounce)
            {
                state = HoverState.Idle;
                return;
            }

            state = HoverState.ExitingBounce;
            stateTimer = 0f;
        }
    }
}