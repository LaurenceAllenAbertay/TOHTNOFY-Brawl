using UnityEngine;
using System.Collections;

namespace DDD.TNFY.BRAWL
{
    public class CameraController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 10f;
        [SerializeField] private float focusTransitionDuration = 1f;
        [SerializeField] private float cameraZOffset = 3.5f; 

        [Header("Bounds")]
        [SerializeField] private BoxCollider boundsBox;
        [SerializeField] private float boundsPadding = 2f;
        [SerializeField] private bool visualizeBounds = true;

        private Camera cam;
        private bool isTransitioning = false;

        void Start()
        {
            cam = GetComponent<Camera>();

            // Auto-find bounds if not assigned
            if (boundsBox == null)
            {
                Debug.LogWarning("CameraController: No BoxCollider found for bounds. Camera movement will be unbounded.");
            }

            // Subscribe to turn change events
            TurnManager.OnTurnStarted += OnTurnStarted;

            // NEW: Focus on current unit immediately if game already started
            var turnManager = FindAnyObjectByType<TurnManager>();
            if (turnManager != null && turnManager.CurrentUnit != null)
            {
                FocusOnUnitImmediate(turnManager.CurrentUnit);
            }
        }


        void OnDestroy()
        {
            TurnManager.OnTurnStarted -= OnTurnStarted;
        }

        void Update()
        {
            // Only allow manual movement when not transitioning
            if (!isTransitioning)
            {
                HandleMovement();
            }
        }

        private void FocusOnUnitImmediate(Unit targetUnit)
        {
            if (targetUnit == null || targetUnit.currentTile == null) return;

            Vector3 targetPosition = new Vector3(
                targetUnit.transform.position.x,
                targetUnit.transform.position.y + 2f, // Unit's Y + 2
                targetUnit.transform.position.z - cameraZOffset
            );

            targetPosition = ClampToBounds(targetPosition);
            transform.position = targetPosition;
        }

        void HandleMovement()
        {
            // Get input
            Vector3 movement = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) movement.z += 1f;
            if (Input.GetKey(KeyCode.S)) movement.z -= 1f;
            if (Input.GetKey(KeyCode.A)) movement.x -= 1f;
            if (Input.GetKey(KeyCode.D)) movement.x += 1f;

            // Y-axis movement
            if (Input.GetKey(KeyCode.E)) movement.y += 1f;
            if (Input.GetKey(KeyCode.Q)) movement.y -= 1f;

            // Normalize diagonal movement and apply speed
            movement = movement.normalized * moveSpeed * Time.deltaTime;

            // Calculate new position
            Vector3 newPosition = transform.position + movement;

            // Apply bounds checking
            newPosition = ClampToBounds(newPosition);

            // Apply the movement
            transform.position = newPosition;
        }
        public Vector3 ClampToBounds(Vector3 position)
        {
            if (boundsBox == null) return position;

            Bounds bounds = boundsBox.bounds;

            position.x = Mathf.Clamp(position.x,
                bounds.min.x + boundsPadding,
                bounds.max.x - boundsPadding);
            position.z = Mathf.Clamp(position.z,
                bounds.min.z + boundsPadding,
                bounds.max.z - boundsPadding);
            position.y = Mathf.Clamp(position.y,
                bounds.min.y + boundsPadding,
                bounds.max.y - boundsPadding);

            return position;
        }

        // Event handler for turn changes
        private void OnTurnStarted(Unit newActiveUnit)
        {
            if (newActiveUnit != null)
            {
                StartCoroutine(SmoothFocusOnUnit(newActiveUnit));
            }
        }

        // Smooth camera transition to focus on a unit
        private IEnumerator SmoothFocusOnUnit(Unit targetUnit)
        {
            if (targetUnit == null || targetUnit.currentTile == null) yield break;

            isTransitioning = true;

            Vector3 startPosition = transform.position;
            Vector3 targetPosition = new Vector3(
                targetUnit.transform.position.x,
                targetUnit.transform.position.y + 2f, // Unit's Y + 2
                targetUnit.transform.position.z - cameraZOffset
            );

            // Apply bounds checking to target position
            targetPosition = ClampToBounds(targetPosition);

            float elapsed = 0f;

            while (elapsed < focusTransitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / focusTransitionDuration;

                // Use smooth step for eased transition
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);

                yield return null;
            }

            // Ensure exact final position
            transform.position = targetPosition;
            isTransitioning = false;
        }

        // Public methods
        public void FocusOnPosition(Vector3 worldPosition)
        {
            // Don't allow manual focus during automatic transitions
            if (isTransitioning) return;

            Vector3 targetPos = new Vector3(worldPosition.x, transform.position.y, worldPosition.z);
            targetPos = ClampToBounds(targetPos);
            transform.position = targetPos;
        }

        public void SetBoundsCollider(BoxCollider newBounds)
        {
            boundsBox = newBounds;
        }

        // Property to check if camera is currently transitioning
        public bool IsTransitioning => isTransitioning;

        // Debug visualization
        void OnDrawGizmos()
        {
            if (!visualizeBounds || boundsBox == null) return;

            // Draw box collider bounds
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(boundsBox.bounds.center, boundsBox.bounds.size);

            // Draw padding area
            if (boundsPadding > 0)
            {
                Gizmos.color = Color.red;
                Vector3 paddedSize = boundsBox.bounds.size - new Vector3(boundsPadding * 2, 0, boundsPadding * 2);
                Gizmos.DrawWireCube(boundsBox.bounds.center, paddedSize);
            }
        }
    }
}