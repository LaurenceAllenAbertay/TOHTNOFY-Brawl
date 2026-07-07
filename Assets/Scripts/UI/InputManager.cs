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

        [SerializeField] private LayerMask unitLayer;
        
        public static event Action<Vector3> OnMouseMoved;
        public static event Action<Vector3> OnMouseClicked;
        public static event Action<Vector3> OnMouseRightClicked;
        
        public static event Action<int> OnAbilitySelected;
        
        public static event Action OnEscapePressed;
        public static event Action OnEndTurnRequested;
        
        public static event Action<Vector2> OnCameraMove;
        public static event Action<float> OnCameraElevate;
        
        public static event Action<Unit> OnUnitHovered;
        public static event Action<Unit> OnUnitHoverExited;

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

        private Tile hoveredTile;

        private Unit _hoveredUnit;

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
            if (!gameplayMap.enabled) return;

            Vector2 screenPos = mousePositionAction.ReadValue<Vector2>();

            Tile tileUnderCursor = RaycastTile(screenPos);
            UpdateHoveredTile(tileUnderCursor);

            Unit unitUnderCursor = RaycastUnit(screenPos);
            UpdateHoveredUnit(unitUnderCursor);

            OnMouseMoved?.Invoke(ScreenToWorld(screenPos));

            OnCameraMove?.Invoke(cameraMoveAction.ReadValue<Vector2>());
            OnCameraElevate?.Invoke(cameraElevateAction.ReadValue<float>());
        }

        private void OnClick(InputAction.CallbackContext ctx)
        {
            if (IsMouseOverUI()) return;

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

        private Tile RaycastTile(Vector2 screenPos)
        {
            if (mainCamera == null || GridManager.Instance == null) return null;
            if (IsMouseOverUI()) return null;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, GridManager.Instance.tileLayer))
                return hit.collider.GetComponent<Tile>();

            return null;
        }

        private void UpdateHoveredTile(Tile newTile)
        {
            if (newTile == hoveredTile) return;

            if (hoveredTile != null)
                Tile.NotifyHoverExited(hoveredTile);

            hoveredTile = newTile;

            if (hoveredTile != null)
                Tile.NotifyHovered(hoveredTile);
        }
        
        private Unit RaycastUnit(Vector2 screenPos)
        {
            if (mainCamera == null) return null;
            if (IsMouseOverUI()) return null;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, unitLayer,
                                 QueryTriggerInteraction.Collide))
            {
                return hit.collider.GetComponentInParent<Unit>();
            }

            return null;
        }

        private void UpdateHoveredUnit(Unit newUnit)
        {
            if (newUnit == _hoveredUnit) return;

            if (_hoveredUnit != null)
                OnUnitHoverExited?.Invoke(_hoveredUnit);

            _hoveredUnit = newUnit;

            if (_hoveredUnit != null)
                OnUnitHovered?.Invoke(_hoveredUnit);
        }

        private Vector3 ScreenToWorld(Vector2 screenPos)
        {
            if (mainCamera == null) return Vector3.zero;

            Ray ray = mainCamera.ScreenPointToRay(screenPos);
            
            if (GridManager.Instance != null &&
                Physics.Raycast(ray, out RaycastHit tileHit, Mathf.Infinity, GridManager.Instance.tileLayer))
            {
                return tileHit.point;
            }

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