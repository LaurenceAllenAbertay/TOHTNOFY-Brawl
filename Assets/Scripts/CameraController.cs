using UnityEngine;
using System.Collections;

namespace DDD.TNFY.BRAWL
{
    public class CameraController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float focusTransitionDuration = 1f;
        [SerializeField] private float cameraZOffset = 3.5f;
        [SerializeField] private float cameraYOffset = 2f;

        [Header("AOE Zoom Settings")]
        [SerializeField] private float aoeZoomYMultiplier = 0.8f;
        [SerializeField] private float aoeZoomZMultiplier = 2f;

        [Header("Bounds")]
        [SerializeField] private BoxCollider boundsBox;
        [SerializeField] private float boundsPadding = 1f;
        [SerializeField] private bool visualizeBounds = false;

        [Header("Screen Shake")]
        [SerializeField] private float shakeMaxMagnitude = 0.35f;
        [SerializeField] private float shakeMaxDuration = 0.4f;
        [SerializeField] private float shakeDamageReference = 10f;

        private Camera cam;
        private bool isTransitioning = false;

        // True only during a PlayerUnit's turn. Set by OnTurnStarted so manual
        // camera movement is blocked while enemies or AI-controlled units are acting.
        private bool _playerInputEnabled = false;

        // ── Screen shake state ────────────────────────────────────────────────────
        private Coroutine _shakeCoroutine;
        private float _currentShakeMagnitude = 0f;

        /// <summary>
        /// Singleton reference. Set in Awake — safe to read from any Start or later.
        /// Use CameraController.Instance?.Shake(...) to trigger a shake from anywhere.
        /// </summary>
        public static CameraController Instance { get; private set; }

        void Awake()
        {
            Instance = this;
            cam = GetComponent<Camera>();
        }

        void Start()
        {
            if (boundsBox == null)
                Debug.LogWarning("CameraController: No BoxCollider found for bounds. Camera movement will be unbounded.");

            TurnManager.OnTurnStarted += OnTurnStarted;
            Unit.OnDamageDealt += OnUnitTookDamage;

            var turnManager = FindAnyObjectByType<TurnManager>();
            if (turnManager != null && turnManager.CurrentUnit != null)
                FocusOnUnitImmediate(turnManager.CurrentUnit);
        }

        void OnDestroy()
        {
            TurnManager.OnTurnStarted -= OnTurnStarted;
            Unit.OnDamageDealt -= OnUnitTookDamage;

            if (Instance == this)
                Instance = null;
        }

        void Update()
        {
            if (!isTransitioning && _playerInputEnabled)
                HandleMovement();
        }

        #region Public API

        /// <summary>
        /// Computes the camera world-position that frames a unit, using this controller's
        /// configured Y and Z offsets. All systems should call this instead of hardcoding offsets.
        /// </summary>
        public Vector3 UnitFocusPosition(Unit unit)
        {
            if (unit == null) return transform.position;
            return ClampToBounds(new Vector3(
                unit.transform.position.x,
                unit.transform.position.y + cameraYOffset,
                unit.transform.position.z - cameraZOffset
            ));
        }

        /// <summary>
        /// Computes the camera world-position that frames any world position
        /// using the same offsets as unit focus.
        /// </summary>
        public Vector3 WorldFocusPosition(Vector3 worldPosition)
        {
            return ClampToBounds(new Vector3(
                worldPosition.x,
                worldPosition.y + cameraYOffset,
                worldPosition.z - cameraZOffset
            ));
        }

        /// <summary>
        /// Smoothly transitions the camera to targetPosition over duration seconds.
        /// Blocks manual camera movement for the duration of the transition.
        /// All camera-transition callers (Unit, CombatManager, UIManager) should use this
        /// instead of maintaining their own lerp coroutines.
        /// </summary>
        public IEnumerator TransitionTo(Vector3 targetPosition, float duration = 1f)
        {
            targetPosition = ClampToBounds(targetPosition);

            // Skip if already at the destination to avoid a full-duration pause
            // when the camera is already on the caster before the ability fires.
            if (Vector3.Distance(transform.position, targetPosition) < 0.01f)
                yield break;

            isTransitioning = true;
            Vector3 startPosition = transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                transform.position = Vector3.Lerp(startPosition, targetPosition,
                    Mathf.SmoothStep(0f, 1f, elapsed / duration));
                yield return null;
            }

            transform.position = targetPosition;
            isTransitioning = false;
        }

        public Vector3 ClampToBounds(Vector3 position)
        {
            if (boundsBox == null) return position;

            Bounds bounds = boundsBox.bounds;
            position.x = Mathf.Clamp(position.x, bounds.min.x + boundsPadding, bounds.max.x - boundsPadding);
            position.z = Mathf.Clamp(position.z, bounds.min.z + boundsPadding, bounds.max.z - boundsPadding);
            position.y = Mathf.Clamp(position.y, bounds.min.y + boundsPadding, bounds.max.y - boundsPadding);
            return position;
        }

        public void FocusOnPosition(Vector3 worldPosition)
        {
            if (isTransitioning) return;
            Vector3 targetPos = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);
            transform.position = ClampToBounds(targetPos);
        }

        public void SetBoundsCollider(BoxCollider newBounds)
        {
            boundsBox = newBounds;
        }

        public bool IsTransitioning => isTransitioning;

        /// <summary>
        /// Triggers a screen shake of the given magnitude (world units) decaying to zero
        /// over duration seconds. Safe to call from anywhere via CameraController.Instance?.Shake(...).
        /// If a stronger shake is already in progress, the weaker call is ignored so that
        /// AOE hits on multiple units in the same frame don't cut each other short.
        /// </summary>
        public void Shake(float magnitude, float duration)
        {
            // Never interrupt a larger shake with a smaller one.
            // _currentShakeMagnitude is updated here (not inside the coroutine) so that
            // same-frame AOE calls — which all arrive before any coroutine body runs —
            // compare against the correct value rather than a stale 0.
            if (_shakeCoroutine != null && magnitude <= _currentShakeMagnitude)
                return;

            if (_shakeCoroutine != null)
                StopCoroutine(_shakeCoroutine);

            _currentShakeMagnitude = magnitude;
            _shakeCoroutine = StartCoroutine(ShakeRoutine(magnitude, duration));
        }

        /// <summary>
        /// Computes a camera position that frames a circular area of the given world-space
        /// radius centred on a point — used by RandomAOE to show the full ability spread.
        /// Y and Z pullback are separated because the camera needs to step back much further
        /// on Z to keep forward tiles (in front of the caster) in frame.
        /// Both multipliers are tunable in the Inspector under AOE Zoom Settings.
        /// </summary>
        public Vector3 FitRadius(Vector3 center, float worldRadius)
        {
            return ClampToBounds(new Vector3(
                center.x,
                center.y + cameraYOffset + worldRadius * aoeZoomYMultiplier,
                center.z - cameraZOffset - worldRadius * aoeZoomZMultiplier
            ));
        }

        #endregion

        #region Private

        private void FocusOnUnitImmediate(Unit targetUnit)
        {
            if (targetUnit == null || targetUnit.currentTile == null) return;
            transform.position = UnitFocusPosition(targetUnit);
        }

        /// <summary>
        /// Maps incoming damage to shake magnitude and duration, then fires Shake.
        /// Called for every unit hit — the "don't interrupt a stronger shake" guard in
        /// Shake() ensures AOE multi-hits produce one coherent shake at peak intensity.
        /// </summary>
        private void OnUnitTookDamage(Unit victim, int amount)
        {
            float t = Mathf.Clamp01(amount / shakeDamageReference);
            float magnitude = Mathf.Lerp(0.05f, shakeMaxMagnitude, t);
            float duration  = Mathf.Lerp(0.1f,  shakeMaxDuration,  t);
            Shake(magnitude, duration);
        }

        /// <summary>
        /// Displaces the camera by a random XY offset each frame, with magnitude
        /// decaying smoothly to zero, then snaps back to the pre-shake position.
        /// insideUnitCircle is used rather than insideUnitSphere so the shake stays
        /// in the screen plane and never nudges the camera's depth (Z).
        /// </summary>
        private IEnumerator ShakeRoutine(float magnitude, float duration)
        {
            Vector3 originalPosition = transform.position;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                float currentMagnitude = Mathf.Lerp(magnitude, 0f, t);

                Vector2 circle = Random.insideUnitCircle * currentMagnitude;
                transform.position = originalPosition + new Vector3(circle.x, circle.y, 0f);

                yield return null;
            }

            transform.position = originalPosition;
            _currentShakeMagnitude = 0f;
            _shakeCoroutine = null;
        }

        private void HandleMovement()
        {
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement.z += 1f;
            if (Input.GetKey(KeyCode.S)) movement.z -= 1f;
            if (Input.GetKey(KeyCode.A)) movement.x -= 1f;
            if (Input.GetKey(KeyCode.D)) movement.x += 1f;
            if (Input.GetKey(KeyCode.E)) movement.y += 1f;
            if (Input.GetKey(KeyCode.Q)) movement.y -= 1f;

            movement = movement.normalized * moveSpeed * Time.deltaTime;
            transform.position = ClampToBounds(transform.position + movement);
        }

        private void OnTurnStarted(Unit newActiveUnit)
        {
            _playerInputEnabled = newActiveUnit is PlayerUnit;
            if (newActiveUnit != null)
                StartCoroutine(SmoothFocusOnUnit(newActiveUnit));
        }

        /// <summary>
        /// Delegates to TransitionTo so the logic lives in one place.
        /// </summary>
        private IEnumerator SmoothFocusOnUnit(Unit targetUnit)
        {
            if (targetUnit == null || targetUnit.currentTile == null) yield break;
            yield return StartCoroutine(TransitionTo(UnitFocusPosition(targetUnit), focusTransitionDuration));
        }

        #endregion

        #region Editor Support

        void OnDrawGizmos()
        {
            if (!visualizeBounds || boundsBox == null) return;

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(boundsBox.bounds.center, boundsBox.bounds.size);

            if (boundsPadding > 0)
            {
                Gizmos.color = Color.red;
                Vector3 paddedSize = boundsBox.bounds.size - new Vector3(boundsPadding * 2, 0, boundsPadding * 2);
                Gizmos.DrawWireCube(boundsBox.bounds.center, paddedSize);
            }
        }

        #endregion
    }
}