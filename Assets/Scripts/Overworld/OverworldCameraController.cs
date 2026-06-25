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
        [Tooltip("How quickly the camera catches up to the target. Lower = lazier follow. " +
                 "0.1–0.2 gives a comfortable overworld feel.")]
        [SerializeField] private float smoothTime = 0.15f;

        [Header("Bounds (optional)")]
        [Tooltip("BoxCollider whose bounds the camera position is clamped inside. " +
                 "Leave unassigned for an unbounded camera.")]
        [SerializeField] private BoxCollider boundsBox;

        [SerializeField] private float boundsPadding = 1f;

        // ── Private ───────────────────────────────────────────────────────────

        private Vector3 velocity = Vector3.zero;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Start()
        {
            if (target == null)
            {
                Debug.LogWarning("[OverworldCameraController] No target assigned. " +
                                 "DebugOverworldSpawner should call SetTarget() after spawning.");
                return;
            }

            // Snap immediately to avoid a sweeping intro pan from (0,0,0).
            transform.position = DesiredPosition();
        }

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desired = DesiredPosition();
            Vector3 smoothed = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            transform.position = ClampToBounds(smoothed);
        }

        // ── Private Helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Computes the ideal camera world-position behind and above the target,
        /// using the same offset convention as CameraController.UnitFocusPosition.
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
        /// Snaps the camera to the target with no smoothing. Call this when
        /// first entering a scene or after a teleport to avoid a long sweep.
        /// </summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            velocity = Vector3.zero;
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