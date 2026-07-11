using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UIUpdateSystem : MonoBehaviour
    {
        public static UIUpdateSystem Instance { get; private set; }

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

        public static event System.Action OnTurnUIUpdate;
        public static event System.Action OnAbilityButtonsUpdate;
        public static event System.Action OnHealthBarsUpdate;
        public static event System.Action OnTurnOrderUpdate;
        public static event System.Action OnMovementUpdate;
        public static event System.Action OnTooltipsUpdate;

        public static event System.Action OnAbilityAnimationStarted;
        public static event System.Action OnAbilityAnimationComplete;

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

        public static void MarkForUpdate(UIUpdateFlags flags)
        {
            if (Instance != null)
            {
                Instance.pendingUpdates |= flags;
            }
        }

        public static bool WasUpdatedThisFrame(UIUpdateFlags flags)
        {
            return Instance != null && Instance.hasUpdatesThisFrame && (Instance.pendingUpdates & flags) != 0;
        }

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

        public static void TriggerAbilityAnimationStarted()
        {
            OnAbilityAnimationStarted?.Invoke();
        }

        public static void TriggerAbilityAnimationComplete()
        {
            OnAbilityAnimationComplete?.Invoke();
        }

        public static void TriggerMovementAnimationStarted()
        {
            OnMovementAnimationStarted?.Invoke();
        }

        public static void TriggerMovementAnimationComplete()
        {
            OnMovementAnimationComplete?.Invoke();
        }

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

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public static void DebugLogPendingUpdates()
        {
            if (Instance != null)
                Debug.Log($"Pending UI Updates: {Instance.pendingUpdates}");
        }
    }

    public static class UIEvents
    {
        public static void OnActiveUnitChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        public static void OnUnitHealthChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.HealthBars);
        }

        public static void OnCombatStateChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.AbilityButtons |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        public static void OnAbilityUsed()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.AbilityButtons);
        }

        public static void OnUnitMoved()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        public static void OnTargetingStateChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons |
                                       UIUpdateSystem.UIUpdateFlags.Tooltips);
        }

        public static void OnTurnChanged()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnOrder);
        }

        public static void OnAbilityAnimationStarted()
        {
            UIUpdateSystem.TriggerAbilityAnimationStarted();
        }

        public static void OnAbilityAnimationComplete()
        {
            UIUpdateSystem.TriggerAbilityAnimationComplete();
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons);
        }

        public static void OnMovementAnimationStarted()
        {
            UIUpdateSystem.TriggerMovementAnimationStarted();
        }

        public static void OnMovementAnimationComplete()
        {
            UIUpdateSystem.TriggerMovementAnimationComplete();
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.Movement);
        }

        public static void OnPlayerTurnEnded()
        {
            UIUpdateSystem.MarkForUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                       UIUpdateSystem.UIUpdateFlags.AbilityButtons);
        }
    }
}