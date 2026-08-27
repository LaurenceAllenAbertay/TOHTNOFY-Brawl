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

        private bool _playerInputEnabled = false;

        private Vector2 _cameraMoveInput;
        private float _cameraElevateInput;

        private Coroutine _shakeCoroutine;
        private float _currentShakeMagnitude = 0f;
        
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
            InputManager.OnCameraMove += HandleCameraMoveInput;
            InputManager.OnCameraElevate += HandleCameraElevateInput;

            var turnManager = FindAnyObjectByType<TurnManager>();
            if (turnManager != null && turnManager.CurrentUnit != null)
                FocusOnUnitImmediate(turnManager.CurrentUnit);
        }

        void OnDestroy()
        {
            TurnManager.OnTurnStarted -= OnTurnStarted;
            Unit.OnDamageDealt -= OnUnitTookDamage;
            InputManager.OnCameraMove -= HandleCameraMoveInput;
            InputManager.OnCameraElevate -= HandleCameraElevateInput;

            if (Instance == this)
                Instance = null;
        }

        private void HandleCameraMoveInput(Vector2 moveInput) => _cameraMoveInput = moveInput;
        private void HandleCameraElevateInput(float elevateInput) => _cameraElevateInput = elevateInput;

        public void ClearMovementInput()
        {
            _cameraMoveInput = Vector2.zero;
            _cameraElevateInput = 0f;
        }

        void Update()
        {
            if (!isTransitioning && _playerInputEnabled)
                HandleMovement();
        }

        #region Public API
        
        public Vector3 UnitFocusPosition(Unit unit)
        {
            if (unit == null) return transform.position;
            return ClampToBounds(new Vector3(
                unit.transform.position.x,
                unit.transform.position.y + cameraYOffset,
                unit.transform.position.z - cameraZOffset
            ));
        }

        public Vector3 WorldFocusPosition(Vector3 worldPosition)
        {
            return ClampToBounds(new Vector3(
                worldPosition.x,
                worldPosition.y + cameraYOffset,
                worldPosition.z - cameraZOffset
            ));
        }

        public IEnumerator TransitionTo(Vector3 targetPosition, float duration = 1f)
        {
            targetPosition = ClampToBounds(targetPosition);

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
        
        public void Shake(float magnitude, float duration)
        {
            if (_shakeCoroutine != null && magnitude <= _currentShakeMagnitude)
                return;

            if (_shakeCoroutine != null)
                StopCoroutine(_shakeCoroutine);

            _currentShakeMagnitude = magnitude;
            _shakeCoroutine = StartCoroutine(ShakeRoutine(magnitude, duration));
        }
        
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
        
        private void OnUnitTookDamage(Unit victim, int amount)
        {
            float t = Mathf.Clamp01(amount / shakeDamageReference);
            float magnitude = Mathf.Lerp(0.05f, shakeMaxMagnitude, t);
            float duration  = Mathf.Lerp(0.1f,  shakeMaxDuration,  t);
            Shake(magnitude, duration);
        }
        
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
            Vector3 movement = new Vector3(_cameraMoveInput.x, _cameraElevateInput, _cameraMoveInput.y);

            movement = movement.normalized * moveSpeed * Time.deltaTime;
            transform.position = ClampToBounds(transform.position + movement);
        }

        private void OnTurnStarted(Unit newActiveUnit)
        {
            _playerInputEnabled = newActiveUnit != null && !newActiveUnit.IsAIControlled;
            if (newActiveUnit != null)
                StartCoroutine(SmoothFocusOnUnit(newActiveUnit));
        }

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