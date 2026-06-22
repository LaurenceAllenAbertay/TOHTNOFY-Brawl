using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace DDD.TNFY.BRAWL
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private Camera mainCamera;

        // Mouse events
        public static event Action<Vector3> OnMouseMoved;
        public static event Action<Vector3> OnMouseClicked;
        public static event Action<Vector3> OnMouseRightClicked;

        // Ability events — slot index replaces KeyCode
        public static event Action<int> OnAbilitySelected;

        // Action events
        public static event Action OnEscapePressed;
        public static event Action OnEndTurnRequested;

        // Camera events
        public static event Action<Vector2> OnCameraMove;
        public static event Action<float> OnCameraElevate;

        private InputActionMap gameplayMap;
        private InputAction clickAction;
        private InputAction rightClickAction;
        private InputAction mousePositionAction;
        private InputAction ability1Action;
        private InputAction ability2Action;
        private InputAction ability3Action;
        private InputAction cancelAction;
        private InputAction endTurnAction;
        private InputAction cameraMoveAction;
        private InputAction cameraElevateAction;

        // The tile the mouse is currently over, tracked via tile-layer raycast each frame.
        // Kept here so hover enter/exit events fire correctly as the cursor moves.
        private Tile hoveredTile;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            if (mainCamera == null)
                mainCamera = Camera.main;

            gameplayMap = inputActions.FindActionMap("Gameplay", throwIfNotFound: true);

            clickAction         = gameplayMap.FindAction("Click",         throwIfNotFound: true);
            rightClickAction    = gameplayMap.FindAction("RightClick",    throwIfNotFound: true);
            mousePositionAction = gameplayMap.FindAction("MousePosition", throwIfNotFound: true);
            ability1Action      = gameplayMap.FindAction("Ability1",      throwIfNotFound: true);
            ability2Action      = gameplayMap.FindAction("Ability2",      throwIfNotFound: true);
            ability3Action      = gameplayMap.FindAction("Ability3",      throwIfNotFound: true);
            cancelAction        = gameplayMap.FindAction("Cancel",        throwIfNotFound: true);
            endTurnAction       = gameplayMap.FindAction("EndTurn",       throwIfNotFound: true);
            cameraMoveAction    = gameplayMap.FindAction("CameraMove",    throwIfNotFound: true);
            cameraElevateAction = gameplayMap.FindAction("CameraElevate", throwIfNotFound: true);
        }

        private void OnEnable()
        {
            gameplayMap.Enable();

            clickAction.performed         += OnClick;
            rightClickAction.performed    += OnRightClick;
            ability1Action.performed      += OnAbility1;
            ability2Action.performed      += OnAbility2;
            ability3Action.performed      += OnAbility3;
            cancelAction.performed        += OnCancel;
            endTurnAction.performed       += OnEndTurn;
        }

        private void OnDisable()
        {
            clickAction.performed         -= OnClick;
            rightClickAction.performed    -= OnRightClick;
            ability1Action.performed      -= OnAbility1;
            ability2Action.performed      -= OnAbility2;
            ability3Action.performed      -= OnAbility3;
            cancelAction.performed        -= OnCancel;
            endTurnAction.performed       -= OnEndTurn;

            gameplayMap.Disable();
        }

        private void Update()
        {
            // Only broadcast mouse movement while the action map is active.
            // This silences subscribers during cutscenes or when input is globally disabled.
            if (!gameplayMap.enabled) return;

            Vector2 screenPos = mousePositionAction.ReadValue<Vector2>();

            // Resolve the tile under the cursor using a tile-layer-only raycast so that
            // wall/occluder colliders on other layers cannot intercept hover or click events.
            Tile tileUnderCursor = RaycastTile(screenPos);
            UpdateHoveredTile(tileUnderCursor);

            OnMouseMoved?.Invoke(ScreenToWorld(screenPos));

            OnCameraMove?.Invoke(cameraMoveAction.ReadValue<Vector2>());
            OnCameraElevate?.Invoke(cameraElevateAction.ReadValue<float>());
        }

        // --- Action callbacks ---

        private void OnClick(InputAction.CallbackContext ctx)
        {
            if (IsMouseOverUI()) return;

            // Fire the tile clicked event directly from the tile-layer raycast result so
            // wall colliders cannot block clicks on tiles behind/beneath them.
            if (hoveredTile != null)
                Tile.NotifyClicked(hoveredTile);

            OnMouseClicked?.Invoke(ScreenToWorld(mousePositionAction.ReadValue<Vector2>()));
        }

        private void OnRightClick(InputAction.CallbackContext ctx)
        {
            if (IsMouseOverUI()) return;
            OnMouseRightClicked?.Invoke(ScreenToWorld(mousePositionAction.ReadValue<Vector2>()));
        }

        private void OnAbility1(InputAction.CallbackContext ctx) => OnAbilitySelected?.Invoke(0);
        private void OnAbility2(InputAction.CallbackContext ctx) => OnAbilitySelected?.Invoke(1);
        private void OnAbility3(InputAction.CallbackContext ctx) => OnAbilitySelected?.Invoke(2);

        private void OnCancel(InputAction.CallbackContext ctx) => OnEscapePressed?.Invoke();

        private void OnEndTurn(InputAction.CallbackContext ctx) => OnEndTurnRequested?.Invoke();

        // --- Tile hover tracking -----------------------------------------------

        /// <summary>
        /// Raycasts against the tile layer only and returns whichever Tile was hit,
        /// or null when the cursor is not over any tile.
        /// </summary>
        private Tile RaycastTile(Vector2 screenPos)
        {
            if (mainCamera == null || GridManager.Instance == null) return null;
            if (IsMouseOverUI()) return null;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, GridManager.Instance.tileLayer))
                return hit.collider.GetComponent<Tile>();

            return null;
        }

        /// <summary>
        /// Compares the newly raycasted tile against the last known hovered tile and fires
        /// OnTileHovered / OnTileHoverExited as the cursor moves between tiles (or off all tiles).
        /// </summary>
        private void UpdateHoveredTile(Tile newTile)
        {
            if (newTile == hoveredTile) return;

            if (hoveredTile != null)
                Tile.NotifyHoverExited(hoveredTile);

            hoveredTile = newTile;

            if (hoveredTile != null)
                Tile.NotifyHovered(hoveredTile);
        }

        // --- Utilities ---

        private Vector3 ScreenToWorld(Vector2 screenPos)
        {
            if (mainCamera == null) return Vector3.zero;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);

            // Prefer actual tile collider hit so hover targeting works correctly on multi-level maps.
            if (GridManager.Instance != null &&
                Physics.Raycast(ray, out RaycastHit tileHit, Mathf.Infinity, GridManager.Instance.tileLayer))
            {
                return tileHit.point;
            }

            // Fallback for non-tile contexts.
            if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float dist))
                return ray.GetPoint(dist);
            return Vector3.zero;
        }

        private bool IsMouseOverUI() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        public static bool IsMouseOverUI_Static() =>
            EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}