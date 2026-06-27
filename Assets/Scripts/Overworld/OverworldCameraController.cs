using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Smooth-follow camera for the overworld scene.
    ///
    /// ── Design ───────────────────────────────────────────────────────────────
    /// Uses the same Y and Z offset conventions as CameraController (combat) so
    /// the angle feels identical between scenes. The camera follows the overworld
    /// lead character in real-time using SmoothDamp rather than coroutine
    /// transitions — appropriate for continuous movement vs turn-by-turn focus.
    ///
    /// ── Lookahead ────────────────────────────────────────────────────────────
    /// Each frame the leader's XZ velocity is estimated from positional delta.
    /// A separate SmoothDamp drives _lookaheadOffset toward (direction * distance)
    /// while moving and back to Vector3.zero when idle, so the camera anticipates
    /// direction of travel and gently recenters when the player stops — no extra
    /// code required for the recenter.
    ///
    /// ── Field of View Breathing ──────────────────────────────────────────────
    /// A subtle FOV increase during movement (baseFov → movingFov) reinforces
    /// momentum without any zoom logic. It fades back on idle through a separate
    /// SmoothDamp so it never feels jarring.
    ///
    /// ── Bounds ───────────────────────────────────────────────────────────────
    /// Assign an optional BoxCollider (trigger or solid, doesn't matter) to
    /// boundsBox. The camera position is clamped inside it so it never shows
    /// outside the map. Leave unassigned for an unbounded overworld.
    ///
    /// ── Setup ────────────────────────────────────────────────────────────────
    /// Attach to the Camera GameObject in the overworld scene.
    /// Assign the scene's OverworldCameraController 'target' via SetTarget() —
    /// DebugOverworldSpawner calls this automatically after spawning the party.
    /// Set cameraYOffset and cameraZOffset to match the combat scene camera
    /// (defaults: Y=2, Z=3.5 — same as CameraController).
    ///
    /// ── No Dependency on CameraController ────────────────────────────────────
    /// CameraController is coupled to TurnManager.OnTurnStarted and combat-scene
    /// logic. This is a clean, independent script with no combat dependencies.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class OverworldCameraController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Target")]
        [Tooltip("The lead overworld character's Transform to follow. " +
                 "Assigned at runtime by DebugOverworldSpawner via SetTarget().")]
        [SerializeField] private Transform target;

        [Header("Offset — match your combat CameraController values")]
        [Tooltip("Height above the target. CameraController default: 2.")]
        [SerializeField] private float cameraYOffset = 2f;

        [Tooltip("Pull-back behind the target on the Z axis. CameraController default: 3.5.")]
        [SerializeField] private float cameraZOffset = 3.5f;

        [Header("Smoothing")]
        [Tooltip("How quickly the camera body catches up to the target. Lower = lazier follow. " +
                 "0.1–0.2 gives a comfortable overworld feel.")]
        [SerializeField] private float smoothTime = 0.15f;

        [Header("Lookahead")]
        [Tooltip("How far ahead of the player the camera peeks in the direction of travel. " +
                 "Set to 0 to disable lookahead entirely.")]
        [SerializeField] private float lookaheadDistance = 1.5f;

        [Tooltip("How long the lookahead offset takes to build up and decay back to zero on idle. " +
                 "Higher = more gradual anticipation and recenter when the player stops.")]
        [SerializeField] private float lookaheadSmoothTime = 0.4f;

        [Tooltip("Minimum speed (world units/sec) the leader must reach before lookahead " +
                 "activates. Prevents the camera drifting from micro-inputs or analogue stick noise.")]
        [SerializeField] private float lookaheadMinSpeed = 0.5f;

        [Header("Field of View")]
        [Tooltip("Camera FOV when the player is idle.")]
        [SerializeField] private float baseFov = 60f;

        [Tooltip("Camera FOV at full movement speed. A subtle push (e.g. 60 → 65) " +
                 "adds a pleasant sense of momentum without visible zoom.")]
        [SerializeField] private float movingFov = 65f;

        [Tooltip("Leader speed (world units/sec) that maps to full movingFov. " +
                 "Match this to OverworldPartyManager.moveSpeed (default 5).")]
        [SerializeField] private float fovMaxSpeed = 5f;

        [Tooltip("How quickly the FOV transitions between idle and movement values.")]
        [SerializeField] private float fovSmoothTime = 0.4f;

        [Header("Bounds (optional)")]
        [Tooltip("BoxCollider whose bounds the camera position is clamped inside. " +
                 "Leave unassigned for an unbounded camera.")]
        [SerializeField] private BoxCollider boundsBox;

        [SerializeField] private float boundsPadding = 1f;

        // ── Private ───────────────────────────────────────────────────────────

        private Camera _cam;

        // SmoothDamp ref for the main position follow.
        private Vector3 _velocity = Vector3.zero;

        // Lookahead state — updated each LateUpdate from target position delta.
        private Vector3 _targetPrevPos;
        private Vector3 _lookaheadOffset;
        private Vector3 _lookaheadVelocity;

        // FOV state.
        private float _currentFov;
        private float _fovVelocity;

        // ── Lifecycle ─────────────────────────────────────────────────────────

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

            // Snap immediately so the camera doesn't sweep in from (0,0,0).
            SnapToTarget();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            // ── Velocity estimation ────────────────────────────────────────────
            // Derive flat (XZ) speed from positional delta. Y is excluded so
            // vertical terrain changes don't accidentally tilt the lookahead.
            Vector3 rawDelta  = target.position - _targetPrevPos;
            _targetPrevPos    = target.position;
            Vector3 flatDelta = new Vector3(rawDelta.x, 0f, rawDelta.z);
            float   flatSpeed = flatDelta.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);

            // ── Lookahead ──────────────────────────────────────────────────────
            // Drive a separate offset toward (direction * distance) while moving,
            // or back to zero when idle. SmoothDamp handles both transitions.
            Vector3 targetLookahead = (lookaheadDistance > 0f && flatSpeed > lookaheadMinSpeed)
                ? flatDelta.normalized * lookaheadDistance
                : Vector3.zero;

            _lookaheadOffset = Vector3.SmoothDamp(
                _lookaheadOffset, targetLookahead,
                ref _lookaheadVelocity, lookaheadSmoothTime);

            // ── Position ───────────────────────────────────────────────────────
            Vector3 desired  = DesiredPosition() + _lookaheadOffset;
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref _velocity, smoothTime);
            transform.position = ClampToBounds(smoothed);

            // ── Field of View ──────────────────────────────────────────────────
            float speedRatio = Mathf.Clamp01(flatSpeed / Mathf.Max(fovMaxSpeed, 1e-4f));
            float targetFov  = Mathf.Lerp(baseFov, movingFov, speedRatio);
            _currentFov      = Mathf.SmoothDamp(_currentFov, targetFov, ref _fovVelocity, fovSmoothTime);
            _cam.fieldOfView = _currentFov;
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Computes the base camera world-position behind and above the target,
        /// using the same offset convention as CameraController.UnitFocusPosition.
        /// The lookahead bias is added on top of this in LateUpdate.
        /// </summary>
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

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Snaps the camera to the target with no smoothing and clears all dynamic
        /// state — lookahead offset, FOV, and position SmoothDamp velocity.
        /// Call when first entering a scene or after a teleport to avoid sweeping.
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null) return;

            // Reset all SmoothDamp and lookahead state so nothing carries over.
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