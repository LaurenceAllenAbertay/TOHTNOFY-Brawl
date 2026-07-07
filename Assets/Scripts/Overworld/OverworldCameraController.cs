using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(Camera))]
    public class OverworldCameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Header("Offset")]
        [SerializeField] private float cameraYOffset = 2f;
        
        [SerializeField] private float cameraZOffset = 3.5f;

        [Header("Smoothing")]
        [SerializeField] private float smoothTime = 0.15f;

        [Header("Lookahead")]
        [SerializeField] private float lookaheadDistance = 1.5f;

        [SerializeField] private float lookaheadSmoothTime = 0.4f;

        [SerializeField] private float lookaheadMinSpeed = 0.5f;

        [Header("Field of View")]
        [SerializeField] private float baseFov = 60f;

        [SerializeField] private float movingFov = 65f;
        
        [SerializeField] private float fovMaxSpeed = 5f;

        [SerializeField] private float fovSmoothTime = 0.4f;

        [Header("Bounds")]
        [SerializeField] private BoxCollider boundsBox;

        [SerializeField] private float boundsPadding = 1f;
        
        private Camera _cam;
        
        private Vector3 _velocity = Vector3.zero;
        
        private Vector3 _targetPrevPos;
        private Vector3 _lookaheadOffset;
        private Vector3 _lookaheadVelocity;
        
        private float _currentFov;
        private float _fovVelocity;

        private void Awake()
        {
            _cam             = GetComponent<Camera>();
            _currentFov      = baseFov;
            _cam.fieldOfView = baseFov;
        }

        private void Start()
        {
            if (target == null)
            {
                Debug.LogWarning("[OverworldCameraController] No target assigned. " +
                                 "DebugOverworldSpawner should call SetTarget() after spawning.");
                return;
            }

            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null) return;
            
            Vector3 rawDelta  = target.position - _targetPrevPos;
            _targetPrevPos    = target.position;
            Vector3 flatDelta = new Vector3(rawDelta.x, 0f, rawDelta.z);
            float   flatSpeed = flatDelta.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            
            Vector3 targetLookahead = (lookaheadDistance > 0f && flatSpeed > lookaheadMinSpeed)
                ? flatDelta.normalized * lookaheadDistance
                : Vector3.zero;

            _lookaheadOffset = Vector3.SmoothDamp(
                _lookaheadOffset, targetLookahead,
                ref _lookaheadVelocity, lookaheadSmoothTime);

            Vector3 desired  = DesiredPosition() + _lookaheadOffset;
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime);
            transform.position = ClampToBounds(smoothed);

            float speedRatio = Mathf.Clamp01(flatSpeed / Mathf.Max(fovMaxSpeed, 1e-4f));
            float targetFov  = Mathf.Lerp(baseFov, movingFov, speedRatio);
            _currentFov      = Mathf.SmoothDamp(_currentFov, targetFov, ref _fovVelocity, fovSmoothTime);
            _cam.fieldOfView = _currentFov;
        }

        private Vector3 DesiredPosition()
        {
            Vector3 t = target.position;
            return new Vector3(t.x, t.y + cameraYOffset, t.z - cameraZOffset);
        }

        private Vector3 ClampToBounds(Vector3 position)
        {
            if (boundsBox == null) return position;

            Bounds bounds = boundsBox.bounds;
            position.x = Mathf.Clamp(position.x, bounds.min.x + boundsPadding, bounds.max.x - boundsPadding);
            position.z = Mathf.Clamp(position.z, bounds.min.z + boundsPadding, bounds.max.z - boundsPadding);
            position.y = Mathf.Clamp(position.y, bounds.min.y + boundsPadding, bounds.max.y - boundsPadding);
            return position;
        }
        
        public void SnapToTarget()
        {
            if (target == null) return;
            
            _velocity          = Vector3.zero;
            _lookaheadOffset   = Vector3.zero;
            _lookaheadVelocity = Vector3.zero;
            _targetPrevPos     = target.position;
            _currentFov        = baseFov;
            _fovVelocity       = 0f;

            if (_cam != null) _cam.fieldOfView = baseFov;
            transform.position = ClampToBounds(DesiredPosition());
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            SnapToTarget();
        }

        public void SetBoundsCollider(BoxCollider newBounds)
        {
            boundsBox = newBounds;
        }
    }
}