using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Manages UI update flags to avoid unnecessary updates every frame.
    /// Systems can mark specific UI components as dirty, and they'll be updated once per frame max.
    /// </summary>
    public class UIUpdateSystem : MonoBehaviour
    {
        public static UIUpdateSystem Instance { get; private set; }

        // Update flags for different UI systems
        [System.Flags]
        public enum UIUpdateFlags
        {
            None = 0,
            TurnUI = 1 << 0,
            AbilityButtons = 1 << 1,
            HealthBars = 1 << 2,
            TurnOrder = 1 << 3,
            Movement = 1 << 4,
            Tooltips = 1 << 5,
            All = ~0
        }

        // Events for UI systems to subscribe to
        public static event System.Action OnTurnUIUpdate;
        public static event System.Action OnAbilityButtonsUpdate;
        public static event System.Action OnHealthBarsUpdate;
        public static event System.Action OnTurnOrderUpdate;
        public static event System.Action OnMovementUpdate;
        public static event System.Action OnTooltipsUpdate;

        // Events for animation coordination
        public static event System.Action OnAbilityAnimationStarted;
        public static event System.Action OnAbilityAnimationComplete;

        // Events for movement animation coordination
        public static event System.Action OnMovementAnimationStarted;
        public static event System.Action OnMovementAnimationComplete;

        private UIUpdateFlags pendingUpdates = UIUpdateFlags.None;
        private bool hasUpdatesThisFrame = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void LateUpdate()
        {
            // Process all pending updates once per frame in LateUpdate
            if (pendingUpdates != UIUpdateFlags.None)
            {
                ProcessPendingUpdates();
                pendingUpdates = UIUpdateFlags.None;
                hasUpdatesThisFrame = true;
            }
            else
            {
                hasUpdatesThisFrame = false;
            }
        }

        private void ProcessPendingUpdates()
        {
            if ((pendingUpdates & UIUpdateFlags.TurnUI) != 0)
                OnTurnUIUpdate?.Invoke();

            if ((pendingUpdates & UIUpdateFlags.AbilityButtons) != 0)
                OnAbilityButtonsUpdate?.Invoke();

            if ((pendingUpdates & UIUpdateFlags.HealthBars) != 0)
                OnHealthBarsUpdate?.Invoke();

            if ((pendingUpdates & UIUpdateFlags.TurnOrder) != 0)
                OnTurnOrderUpdate?.Invoke();

            if ((pendingUpdates & UIUpdateFlags.Movement) != 0)
                OnMovementUpdate?.Invoke();

            if ((pendingUpdates & UIUpdateFlags.Tooltips) != 0)
                OnTooltipsUpdate?.Invoke();
        }

        /// <summary>
        /// Mark specific UI components for update. They will be updated once this frame.
        /// </summary>
        public static void MarkForUpdate(UIUpdateFlags flags)
        {
            if (Instance != null)
            {
                Instance.pendingUpdates |= flags;
            }
        }

        /// <summary>
        /// Check if specific UI components were updated this frame.
        /// </summary>
        public static bool WasUpdatedThisFrame(UIUpdateFlags flags)
        {
            return Instance != null && Instance.hasUpdatesThisFrame && (Instance.pendingUpdates & flags) != 0;
        }

        /// <summary>
        /// Force immediate update of specific UI components (use sparingly).
        /// </summary>
        public static void ForceImmediateUpdate(UIUpdateFlags flags)
        {
            if (Instance == null) return;

            if ((flags & UIUpdateFlags.TurnUI) != 0)
                OnTurnUIUpdate?.Invoke();

            if ((flags & UIUpdateFlags.AbilityButtons) != 0)
                OnAbilityButtonsUpdate?.Invoke();

            if ((flags & UIUpdateFlags.HealthBars) != 0)
                OnHealthBarsUpdate?.Invoke();

            if ((flags & UIUpdateFlags.TurnOrder) != 0)
                OnTurnOrderUpdate?.Invoke();

            if ((flags & UIUpdateFlags.Movement) != 0)
                OnMovementUpdate?.Invoke();

            if ((flags & UIUpdateFlags.Tooltips) != 0)
                OnTooltipsUpdate?.Invoke();
        }

        /// <summary>
        /// Trigger animation started event
        /// </summary>
        public static void TriggerAbilityAnimationStarted()
        {
            OnAbilityAnimationStarted?.Invoke();
        }

        /// <summary>
        /// Trigger animation complete event
        /// </summary>
        public static void TriggerAbilityAnimationComplete()
        {
            OnAbilityAnimationComplete?.Invoke();
        }

        /// <summary>
        /// Trigger movement animation started event
        /// </summary>
        public static void TriggerMovementAnimationStarted()
        {
            OnMovementAnimationStarted?.Invoke();
        }

        /// <summary>
        /// Trigger movement animation complete event
        /// </summary>
        public static void TriggerMovementAnimationComplete()
        {
            OnMovementAnimationComplete?.Invoke();
        }

        /// <summary>
        /// Clear pending updates (use when changing scenes or major state changes).
        /// </summary>
        public static void ClearPendingUpdates()
        {
            if (Instance != null)
                Instance.pendingUpdates = UIUpdateFlags.None;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Debug methods
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void DebugLogPendingUpdates()
        {
            if (Instance != null)
                Debug.Log($"Pending UI Updates: {Instance.pendingUpdates}");
        }
    }

    /// <summary>
    /// Helper class with common UI update triggers. Other systems can call these methods
    /// instead of directly using MarkForUpdate.
    /// </summary>
    public static class UIEvents
    {
        /// <summary>
        /// Call when the active unit changes
        /// </summary>
        public static void OnActiveUnitChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        /// <summary>
        /// Call when a unit takes damage or dies
        /// </summary>
        public static void OnUnitHealthChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.HealthBars);
        }

        /// <summary>
        /// Call when combat state changes (can use abilities, can move, etc.)
        /// </summary>
        public static void OnCombatStateChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.AbilityButtons |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        /// <summary>
        /// Call when an ability is used and the player can no longer use abilities this turn
        /// </summary>
        public static void OnAbilityUsed()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.AbilityButtons);
        }

        /// <summary>
        /// Call when a unit moves
        /// </summary>
        public static void OnUnitMoved()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        /// <summary>
        /// Call when entering/exiting ability targeting
        /// </summary>
        public static void OnTargetingStateChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons |
                                       UIUpdateSystem.UIUpdateFlags.Tooltips);
        }

        /// <summary>
        /// Call when turn order changes or new turn starts
        /// </summary>
        public static void OnTurnChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnOrder);
        }

        /// <summary>
        /// Call when ability animation starts (hides UI)
        /// </summary>
        public static void OnAbilityAnimationStarted()
        {
            UIUpdateSystem.TriggerAbilityAnimationStarted();
        }

        /// <summary>
        /// Call when ability animation completes (shows UI)
        /// </summary>
        public static void OnAbilityAnimationComplete()
        {
            UIUpdateSystem.TriggerAbilityAnimationComplete();
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons);
        }

        /// <summary>
        /// Call when movement animation starts (hides UI)
        /// </summary>
        public static void OnMovementAnimationStarted()
        {
            UIUpdateSystem.TriggerMovementAnimationStarted();
        }

        /// <summary>
        /// Call when movement animation completes (shows UI)
        /// </summary>
        public static void OnMovementAnimationComplete()
        {
            UIUpdateSystem.TriggerMovementAnimationComplete();
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        /// <summary>
        /// Call when a turn is explicitly ended by the player
        /// Ensures UI state is reset for turn transitions
        /// </summary>
        public static void OnPlayerTurnEnded()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons);
        }
    }
}