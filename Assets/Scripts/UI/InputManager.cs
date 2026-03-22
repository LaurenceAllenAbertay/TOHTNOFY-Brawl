using UnityEngine;
using UnityEngine.EventSystems;

namespace DDD.TNFY.BRAWL
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        // Input events that other systems can subscribe to
        public static event System.Action<Vector3> OnMouseMoved;
        public static event System.Action<Vector3> OnMouseClicked;
        public static event System.Action<Vector3> OnMouseRightClicked;
        public static event System.Action<KeyCode> OnKeyPressed;
        public static event System.Action OnEscapePressed;

        // High priority events that get handled first (for UI systems)
        public static event System.Action OnEscapePressedHighPriority;
        public static event System.Action<Vector3> OnMouseRightClickedHighPriority;

        [Header("Settings")]
        [SerializeField] private float mouseMoveThreshold = 0.1f; // Minimum movement to trigger event
        [SerializeField] private LayerMask raycastLayers = -1; // IDK Like layers to be ignored?

        // State tracking
        private Vector3 lastMouseWorldPosition;
        private bool isMouseOverUI;
        private Camera mainCamera;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            mainCamera = Camera.main;
        }

        private void Update()
        {
            // Track UI state
            isMouseOverUI = IsMouseOverUI();

            // Handle mouse movement
            HandleMouseMovement();

            // Handle mouse clicks
            HandleMouseClicks();

            // Handle keyboard input
            HandleKeyboardInput();
        }

        private void HandleMouseMovement()
        {
            Vector3 currentMouseWorld = GetMouseWorldPosition();

            // Only trigger events if mouse moved significantly and we have a valid position
            if (currentMouseWorld != Vector3.zero &&
                Vector3.Distance(currentMouseWorld, lastMouseWorldPosition) > mouseMoveThreshold)
            {
                lastMouseWorldPosition = currentMouseWorld;
                OnMouseMoved?.Invoke(currentMouseWorld);
            }
        }

        private void HandleMouseClicks()
        {
            if (isMouseOverUI) return; // Don't process clicks over UI

            Vector3 mouseWorldPos = GetMouseWorldPosition();
            if (mouseWorldPos == Vector3.zero) return;

            if (Input.GetMouseButtonDown(0)) // Left click
            {
                OnMouseClicked?.Invoke(mouseWorldPos);
            }

            if (Input.GetMouseButtonDown(1)) // Right click
            {
                // First let high priority systems handle right-click (like UI)
                OnMouseRightClickedHighPriority?.Invoke(mouseWorldPos);

                // Then let normal systems handle it
                OnMouseRightClicked?.Invoke(mouseWorldPos);
            }
        }

        private void HandleKeyboardInput()
        {
            // Handle escape key specially since it's commonly used
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                // First let high priority systems handle ESC (like UI)
                OnEscapePressedHighPriority?.Invoke();

                // Then let normal systems handle it
                OnEscapePressed?.Invoke();
                return; // Don't also send it as a general key press
            }

            // Handle ability hotkeys
            if (Input.GetKeyDown(KeyCode.Alpha1))
                OnKeyPressed?.Invoke(KeyCode.Alpha1);
            else if (Input.GetKeyDown(KeyCode.Alpha2))
                OnKeyPressed?.Invoke(KeyCode.Alpha2);
            else if (Input.GetKeyDown(KeyCode.Alpha3))
                OnKeyPressed?.Invoke(KeyCode.Alpha3);

            // Add more specific keys as needed
        }

        private Vector3 GetMouseWorldPosition()
        {
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

            // First try raycast against tile colliders specifically
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, raycastLayers))
            {
                return hit.point;
            }

            // If no tiles hit, fallback to intelligent ground plane detection
            return GetIntelligentGroundPosition(ray);
        }

        private Vector3 GetIntelligentGroundPosition(Ray ray)
        {
            // Try multiple Y levels to see which one makes sense
            Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

            for (int level = -3; level <= 3; level++) // Check 7 levels around current
            {
                float yLevel = level * tileSpacing.y;
                Plane levelPlane = new Plane(Vector3.up, new Vector3(0, yLevel, 0));

                if (levelPlane.Raycast(ray, out float distance))
                {
                    Vector3 hitPoint = ray.GetPoint(distance);

                    // Check if there are any tiles near this level
                    Tile nearbyTile = GridManager.Instance.GetClosestTile(hitPoint, tileSpacing);
                    if (nearbyTile != null)
                    {
                        return hitPoint;
                    }
                }
            }

            // Ultimate fallback
            return Vector3.zero;
        }

        private bool IsMouseOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        // Static convenience methods for other systems
        public static bool IsMouseOverUI_Static()
        {
            return Instance?.isMouseOverUI ?? false;
        }

        public static Vector3 GetCurrentMouseWorldPosition()
        {
            return Instance?.GetMouseWorldPosition() ?? Vector3.zero;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // Optional: Method to temporarily disable input processing
        public static void SetInputEnabled(bool enabled)
        {
            if (Instance != null)
                Instance.enabled = enabled;
        }
    }
}