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
        private InputActionMap gameplayMouseMap;
        private InputActionMap gameplayKeysMap;
        private InputActionMap consoleMap;

        private InputAction moveAction;
        private InputAction interactAction;
        private InputAction cancelAction;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            overworldMap     = inputActions.FindActionMap("Overworld",     throwIfNotFound: true);
            gameplayMouseMap = inputActions.FindActionMap("GameplayMouse", throwIfNotFound: true);
            gameplayKeysMap  = inputActions.FindActionMap("GameplayKeys",  throwIfNotFound: true);
            consoleMap       = inputActions.FindActionMap("Console",       throwIfNotFound: true);

            moveAction     = overworldMap.FindAction("Move",     throwIfNotFound: true);
            interactAction = overworldMap.FindAction("Interact", throwIfNotFound: true);
            cancelAction   = overworldMap.FindAction("Cancel",   throwIfNotFound: true);
        }

        private void OnEnable()
        {
            gameplayMouseMap.Disable();
            gameplayKeysMap.Disable();
            consoleMap.Disable();
            overworldMap.Enable();

            interactAction.performed += OnInteract;
            cancelAction.performed   += OnCancel;
        }

        private void OnDisable()
        {
            interactAction.performed -= OnInteract;
            cancelAction.performed   -= OnCancel;

            overworldMap.Disable();

            gameplayMouseMap.Enable();
            gameplayKeysMap.Enable();
            consoleMap.Enable();
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