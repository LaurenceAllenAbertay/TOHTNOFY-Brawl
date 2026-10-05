using System;
using System.Collections;
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

        public static event Action OnDialogueAdvance;
        
        private InputActionMap overworldMap;
        private InputActionMap dialogueMap;
        private InputActionMap gameplayMouseMap;
        private InputActionMap gameplayKeysMap;
        private InputActionMap consoleMap;

        private InputAction moveAction;
        private InputAction interactAction;
        private InputAction cancelAction;
        private InputAction advanceAction;

        private Coroutine _mapSwapRoutine;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            overworldMap     = inputActions.FindActionMap("Overworld",     throwIfNotFound: true);
            dialogueMap      = inputActions.FindActionMap("Dialogue",      throwIfNotFound: true);
            gameplayMouseMap = inputActions.FindActionMap("GameplayMouse", throwIfNotFound: true);
            gameplayKeysMap  = inputActions.FindActionMap("GameplayKeys",  throwIfNotFound: true);
            consoleMap       = inputActions.FindActionMap("Console",       throwIfNotFound: true);

            moveAction     = overworldMap.FindAction("Move",     throwIfNotFound: true);
            interactAction = overworldMap.FindAction("Interact", throwIfNotFound: true);
            cancelAction   = overworldMap.FindAction("Cancel",   throwIfNotFound: true);
            advanceAction  = dialogueMap.FindAction("Advance",   throwIfNotFound: true);
        }

        private void OnEnable()
        {
            gameplayMouseMap.Disable();
            gameplayKeysMap.Disable();
            consoleMap.Disable();
            dialogueMap.Disable();
            overworldMap.Enable();

            interactAction.performed += OnInteract;
            cancelAction.performed   += OnCancel;
            advanceAction.performed  += OnAdvance;
        }

        private void OnDisable()
        {
            interactAction.performed -= OnInteract;
            cancelAction.performed   -= OnCancel;
            advanceAction.performed  -= OnAdvance;

            if (_mapSwapRoutine != null)
            {
                StopCoroutine(_mapSwapRoutine);
                _mapSwapRoutine = null;
            }

            overworldMap.Disable();
            dialogueMap.Disable();

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

        public static void EnterDialogueMode()
        {
            if (Instance == null) return;
            Instance.SwapMaps(Instance.overworldMap, Instance.dialogueMap);
        }

        public static void ExitDialogueMode()
        {
            if (Instance == null) return;
            Instance.SwapMaps(Instance.dialogueMap, Instance.overworldMap);
        }

        private void SwapMaps(InputActionMap from, InputActionMap to)
        {
            if (_mapSwapRoutine != null)
                StopCoroutine(_mapSwapRoutine);

            _mapSwapRoutine = StartCoroutine(SwapMapsRoutine(from, to));
        }

        private IEnumerator SwapMapsRoutine(InputActionMap from, InputActionMap to)
        {
            from.Disable();
            OnMoveInput?.Invoke(Vector2.zero);

            yield return null;

            to.Enable();
            _mapSwapRoutine = null;
        }

        private void OnInteract(InputAction.CallbackContext ctx) => OnInteractPressed?.Invoke();
        private void OnCancel(InputAction.CallbackContext ctx)   => OnCancelPressed?.Invoke();
        private void OnAdvance(InputAction.CallbackContext ctx)  => OnDialogueAdvance?.Invoke();
    }
}