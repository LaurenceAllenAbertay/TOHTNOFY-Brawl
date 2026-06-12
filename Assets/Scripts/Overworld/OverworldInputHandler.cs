using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Reads from the "Overworld" action map in the shared InputActionAsset and
    /// broadcasts clean events for overworld systems to consume.
    ///
    /// ── Setup ────────────────────────────────────────────────────────────────
    /// In your InputActionAsset, add a new Action Map called "Overworld" with:
    ///   • "Move"     — Value, Vector2 (WASD + left stick composite)
    ///   • "Interact" — Button (E / South face button)
    ///   • "Cancel"   — Button (Escape / East face button)
    ///
    /// The "Gameplay" action map is automatically disabled while this handler
    /// is active (OnEnable) and re-enabled on destroy so scene transitions are
    /// safe. Assign this component to the same persistent manager GameObject
    /// that holds InputManager, or a dedicated overworld manager GameObject.
    ///
    /// ── Action Map Isolation ─────────────────────────────────────────────────
    /// Only one action map should be active at a time to prevent combat input
    /// events (tile clicks, ability slots) from firing during the overworld.
    /// This handler disables "Gameplay" on enable and restores it on disable,
    /// so loading back into a combat scene re-activates combat input cleanly.
    /// </summary>
    public class OverworldInputHandler : MonoBehaviour
    {
        public static OverworldInputHandler Instance { get; private set; }

        [SerializeField] private InputActionAsset inputActions;

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fires every frame while a movement direction is held.
        /// Vector2 is in the range [-1, 1] on both axes (raw analogue or digital).
        /// </summary>
        public static event Action<Vector2> OnMoveInput;

        /// <summary>Fires once when the Interact button is pressed.</summary>
        public static event Action OnInteractPressed;

        /// <summary>Fires once when the Cancel button is pressed.</summary>
        public static event Action OnCancelPressed;

        // ── Private ───────────────────────────────────────────────────────────

        private InputActionMap overworldMap;
        private InputActionMap gameplayMap;

        private InputAction moveAction;
        private InputAction interactAction;
        private InputAction cancelAction;

        // ── Lifecycle ─────────────────────────────────────────────────────────

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
            // Disable combat input so no stray tile-click or ability events fire.
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

            // Restore combat input — safe to call even if we're transitioning to
            // a non-combat scene because InputManager guards against null maps.
            gameplayMap.Enable();
        }

        private void Update()
        {
            if (!overworldMap.enabled) return;

            // Broadcast movement every frame so OverworldPlayerController can
            // apply it with deltaTime smoothly, rather than only on performed events.
            Vector2 move = moveAction.ReadValue<Vector2>();
            OnMoveInput?.Invoke(move);
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private void OnInteract(InputAction.CallbackContext ctx) => OnInteractPressed?.Invoke();
        private void OnCancel(InputAction.CallbackContext ctx)   => OnCancelPressed?.Invoke();
    }
}