using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DDD.TNFY.BRAWL
{
    public class OverworldInputHandler : MonoBehaviour
    {
        public static OverworldInputHandler Instance { get; private set; }

        [SerializeField] private InputActionAsset inputActions;
        
        public static event Action<Vector2> OnMoveInput;
        
        public static event Action OnInteractPressed;
        
        public static event Action OnCancelPressed;
        
        private InputActionMap overworldMap;
        private InputActionMap gameplayMap;

        private InputAction moveAction;
        private InputAction interactAction;
        private InputAction cancelAction;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            overworldMap = inputActions.FindActionMap("Overworld", throwIfNotFound: true);
            gameplayMap  = inputActions.FindActionMap("Gameplay",  throwIfNotFound: true);

            moveAction     = overworldMap.FindAction("Move",     throwIfNotFound: true);
            interactAction = overworldMap.FindAction("Interact", throwIfNotFound: true);
            cancelAction   = overworldMap.FindAction("Cancel",   throwIfNotFound: true);
        }

        private void OnEnable()
        {
            gameplayMap.Disable();
            overworldMap.Enable();

            interactAction.performed += OnInteract;
            cancelAction.performed   += OnCancel;
        }

        private void OnDisable()
        {
            interactAction.performed -= OnInteract;
            cancelAction.performed   -= OnCancel;

            overworldMap.Disable();

            gameplayMap.Enable();
        }

        private void Update()
        {
            if (!overworldMap.enabled) return;

            Vector2 move = moveAction.ReadValue<Vector2>();
            OnMoveInput?.Invoke(move);
        }

        private void OnInteract(InputAction.CallbackContext ctx) => OnInteractPressed?.Invoke();
        private void OnCancel(InputAction.CallbackContext ctx)   => OnCancelPressed?.Invoke();
    }
}