using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Coordinates HUD visibility and world-space UI positioning.
    /// Delegates turn-order display to TurnOrderUIController and
    /// ability panel management to AbilityPanelController.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        #region Properties

        public bool AreAbilitiesExpanded => abilityPanel != null && abilityPanel.AreAbilitiesExpanded;
        public bool IsUIHiddenForAnimation => uiHiddenForAnimation;
        public bool IsUIHiddenForMovement => uiHiddenForMovement;

        #endregion

        #region Serialized Fields

        [Header("=== HUD Root ===")]
        [SerializeField] private GameObject turnUI;
        [SerializeField] private RectTransform turnUIRectTransform;
        [SerializeField] private Button endTurnButton;
        [Tooltip("The child Image whose colour is tinted to match the active unit. " +
                 "Should NOT be the Button's own Image component.")]
        [SerializeField] private Image endTurnButtonColourIndicator;

        [Header("=== World UI Settings ===")]
        [SerializeField] private Vector2 worldOffset = new Vector2(0f, 0.5f);
        [SerializeField] private float baseUISize = 40f;
        [SerializeField] private float referenceCameraDistance = 10f;
        [SerializeField] private bool maintainScreenSize = false;

        [Header("=== Debug ===")]
        [SerializeField] private bool debugMode = false;
        [SerializeField] private bool forceShowUI = false;

        #endregion

        #region Private Fields

        private TurnManager turnManager;
        private CombatManager combatManager;
        private CameraController cameraController;
        private JumpSystem jumpSystem;
        private Camera mainCamera;
        private Canvas parentCanvas;

        private TurnOrderUIController turnOrderController;
        private AbilityPanelController abilityPanel;

        private Unit currentPlayer;
        private bool uiHiddenForAnimation;
        private bool uiHiddenForMovement;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            ValidateReferences();
        }

        void Start()
        {
            CacheComponents();
            FindGameSystems();
            InitializeSubControllers();
            SubscribeToEvents();

            SetUIVisibility(false);
        }

        void LateUpdate()
        {
            if (turnUI.activeInHierarchy && currentPlayer != null)
                UpdateUIPositionAndScale();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        #endregion

        #region Initialization

        private void ValidateReferences()
        {
            if (turnUI == null)
            {
                Debug.LogError("UIManager: turnUI reference is not set!");
                return;
            }

            if (turnUIRectTransform == null)
                turnUIRectTransform = turnUI.GetComponent<RectTransform>();
        }

        private void CacheComponents()
        {
            mainCamera = Camera.main;
            if (turnUIRectTransform != null)
                parentCanvas = turnUIRectTransform.GetComponentInParent<Canvas>();
        }

        private void FindGameSystems()
        {
            turnManager = FindAnyObjectByType<TurnManager>();
            combatManager = FindAnyObjectByType<CombatManager>();
            cameraController = FindAnyObjectByType<CameraController>();
            jumpSystem = FindAnyObjectByType<JumpSystem>();
        }

        private void InitializeSubControllers()
        {
            turnOrderController = GetComponent<TurnOrderUIController>();
            if (turnOrderController != null)
            {
                turnOrderController.Initialize(
                    turnManager, combatManager,
                    () => uiHiddenForAnimation,
                    () => uiHiddenForMovement);
            }

            abilityPanel = GetComponent<AbilityPanelController>();
            if (abilityPanel != null)
            {
                abilityPanel.Initialize(
                    combatManager,
                    () => uiHiddenForAnimation,
                    () => uiHiddenForMovement,
                    () => currentPlayer);
            }
        }

        private void SubscribeToEvents()
        {
            UIUpdateSystem.OnTurnUIUpdate += HandleTurnUIUpdate;
            UIUpdateSystem.OnAbilityButtonsUpdate += HandleAbilityButtonsUpdate;
            UIUpdateSystem.OnAbilityAnimationStarted += HandleAbilityAnimationStarted;
            UIUpdateSystem.OnAbilityAnimationComplete += HandleAbilityAnimationComplete;
            UIUpdateSystem.OnMovementAnimationStarted += HandleMovementAnimationStarted;
            UIUpdateSystem.OnMovementAnimationComplete += HandleMovementAnimationComplete;

            InputManager.OnEscapePressed += HandleEscapePressed;
            InputManager.OnMouseRightClicked += HandleRightClicked;

            TurnManager.OnTurnStarted += HandleTurnStarted;
            TurnManager.OnTurnNumberChanged += HandleTurnIndexChanged;
        }

        private void UnsubscribeFromEvents()
        {
            if (UIUpdateSystem.Instance != null)
            {
                UIUpdateSystem.OnTurnUIUpdate -= HandleTurnUIUpdate;
                UIUpdateSystem.OnAbilityButtonsUpdate -= HandleAbilityButtonsUpdate;
                UIUpdateSystem.OnAbilityAnimationStarted -= HandleAbilityAnimationStarted;
                UIUpdateSystem.OnAbilityAnimationComplete -= HandleAbilityAnimationComplete;
                UIUpdateSystem.OnMovementAnimationStarted -= HandleMovementAnimationStarted;
                UIUpdateSystem.OnMovementAnimationComplete -= HandleMovementAnimationComplete;
            }

            if (InputManager.Instance != null)
            {
                InputManager.OnEscapePressed -= HandleEscapePressed;
                InputManager.OnMouseRightClicked -= HandleRightClicked;
            }

            TurnManager.OnTurnStarted -= HandleTurnStarted;
            TurnManager.OnTurnNumberChanged -= HandleTurnIndexChanged;
        }

        #endregion

        #region Event Handlers

        private void HandleTurnStarted(Unit unit)
        {
            TurnLifecycleLog($"[1/4] HandleTurnStarted → unit={unit?.name ?? "NULL"} " +
                             $"type={unit?.GetType().Name ?? "NULL"} " +
                             $"currentPlayer_before={currentPlayer?.name ?? "NULL"}");

            currentPlayer = unit;
            turnOrderController?.HandleTurnStarted(unit);

            if (unit is PlayerUnit)
            {
                TurnLifecycleLog($"[2/4] HandleTurnStarted → PlayerUnit confirmed, " +
                                 $"launching DelayedTurnUISetup coroutine");

                // Hide the button and HUD immediately so they don't linger during the
                // camera transition. DelayedTurnUISetup will re-show them once the
                // camera has settled on the new active unit.
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
                SetUIVisibility(false);

                abilityPanel?.HandleTurnStarted();
                StartCoroutine(DelayedTurnUISetup());
                StartCoroutine(TurnUIWatchdog(unit));
            }
            else
            {
                TurnLifecycleLog($"[2/4] HandleTurnStarted → Non-player unit, hiding UI");
                SetUIVisibility(false);
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
            }
        }

        private IEnumerator DelayedTurnUISetup()
        {
            // Snapshot the unit this coroutine was created for.
            // HandleTurnStarted overwrites the shared `currentPlayer` field every time a new
            // turn begins (e.g. when endTurnOnCast fires and the next unit's OnTurnStarted
            // arrives before this coroutine resumes). Without the snapshot the check below
            // would evaluate against whichever unit is *currently* active, not the one whose
            // turn this coroutine was spawned for — causing the UI to be silently skipped.
            Unit unitForThisTurn = currentPlayer;

            TurnLifecycleLog($"[3/4] DelayedTurnUISetup START → unitForThisTurn={unitForThisTurn?.name ?? "NULL"} " +
                             $"combatState={combatManager?.currentState}");

            yield return null;

            int waitFrames = 0;
            while (combatManager != null && combatManager.currentState == CombatState.CameraTransition)
            {
                waitFrames++;
                if (debugMode && waitFrames % 10 == 0)
                    TurnLifecycleLog($"[3/4] DelayedTurnUISetup WAITING on CameraTransition → " +
                                     $"frame_count={waitFrames} cameraIsTransitioning={cameraController?.IsTransitioning}");
                yield return null;
            }

            TurnLifecycleLog($"[3/4] DelayedTurnUISetup DONE WAITING → waited {waitFrames} extra frames " +
                             $"combatState={combatManager?.currentState} " +
                             $"currentPlayer={currentPlayer?.name ?? "NULL"} " +
                             $"unitForThisTurn={unitForThisTurn?.name ?? "NULL"} " +
                             $"stillPlayerUnit={unitForThisTurn is PlayerUnit}");

            if (unitForThisTurn is PlayerUnit)
            {
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
                UpdateEndTurnButtonColour(unitForThisTurn);
                SetUIVisibility(true);
                TurnLifecycleLog($"[4/4] DelayedTurnUISetup → SetUIVisibility(true) called. " +
                                 $"turnUI.activeSelf={turnUI?.activeSelf}");
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();
            }
            else
            {
                TurnLifecycleLog($"[4/4] DelayedTurnUISetup → unitForThisTurn is no longer a PlayerUnit — UI skipped. " +
                                 $"This can happen if a rapid turn transition replaced currentPlayer before this coroutine resumed.");
            }
        }

        private void HandleTurnIndexChanged(int totalTurns, int currentIndex)
        {
            turnOrderController?.HandleTurnIndexChanged(totalTurns, currentIndex);
        }

        private void HandleTurnUIUpdate()
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;

            if (ShouldShowUI())
            {
                if (endTurnButton != null)
                {
                    endTurnButton.gameObject.SetActive(currentPlayer is PlayerUnit);
                    if (currentPlayer is PlayerUnit)
                        UpdateEndTurnButtonColour(currentPlayer);
                }
                SetUIVisibility(true);
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();
            }
            else
            {
                SetUIVisibility(false);
                abilityPanel?.CollapseIfExpanded();
            }
        }

        private void HandleAbilityButtonsUpdate()
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;
            if (turnUI.activeInHierarchy && AreAbilitiesExpanded)
                abilityPanel?.UpdateAbilityButtonStates();
        }

        private void HandleAbilityAnimationStarted()
        {
            uiHiddenForAnimation = true;
            SetUIVisibility(false);
            abilityPanel?.CollapseIfExpanded();
        }

        private void HandleAbilityAnimationComplete()
        {
            uiHiddenForAnimation = false;
            if (ShouldShowUI())
            {
                SetUIVisibility(true);
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();
            }
        }

        private void HandleMovementAnimationStarted()
        {
            uiHiddenForMovement = true;
            SetUIVisibility(false);
            abilityPanel?.CollapseIfExpanded();
            if (debugMode) Debug.Log("UIManager: UI hidden for movement");
        }

        private void HandleMovementAnimationComplete()
        {
            uiHiddenForMovement = false;
            if (ShouldShowUI())
            {
                SetUIVisibility(true);
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();
            }
            if (debugMode) Debug.Log("UIManager: UI shown after movement");
        }

        private void HandleEscapePressed()
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;
            if (!IsCurrentlyTargeting())
                abilityPanel?.CollapseIfExpanded();
        }

        private void HandleRightClicked(Vector3 mouseWorldPosition)
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;
            if (!IsCurrentlyTargeting())
                abilityPanel?.CollapseIfExpanded();
        }

        #endregion

        #region World UI Positioning

        private void UpdateUIPositionAndScale()
        {
            if (currentPlayer == null || mainCamera == null || turnUIRectTransform == null || parentCanvas == null)
                return;

            Vector3 worldPosition = currentPlayer.transform.position
                + Vector3.up * worldOffset.y
                + Vector3.right * worldOffset.x;

            Vector3 viewportPosition = mainCamera.WorldToViewportPoint(worldPosition);

            if (viewportPosition.z <= 0)
            {
                // Only suppress the UI if the camera is fully settled. Check both the
                // CombatManager state flag AND cameraController.IsTransitioning directly:
                // the state flag alone is insufficient if WaitForCameraTransition exited
                // early (before isTransitioning was set), leaving currentState as
                // WaitingForInput while the camera is still physically moving.
                bool cameraMoving = cameraController != null && cameraController.IsTransitioning;
                bool inCameraState = combatManager != null && combatManager.currentState == CombatState.CameraTransition;
                if (!cameraMoving && !inCameraState)
                    SetUIVisibility(false);
                return;
            }

            RectTransform canvasRect = parentCanvas.GetComponent<RectTransform>();
            Vector2 canvasSize = canvasRect.sizeDelta;
            Vector2 canvasPosition = new Vector2(
                (viewportPosition.x - 0.5f) * canvasSize.x,
                (viewportPosition.y - 0.5f) * canvasSize.y);

            turnUIRectTransform.anchoredPosition = canvasPosition;

            if (!maintainScreenSize)
            {
                float distanceToCamera = Vector3.Distance(mainCamera.transform.position, worldPosition);
                float scaleFactor = referenceCameraDistance / distanceToCamera;
                turnUIRectTransform.localScale = Vector3.one * (baseUISize / 100f) * scaleFactor;
            }
            else
            {
                turnUIRectTransform.localScale = Vector3.one * (baseUISize / 100f);
            }
        }

        #endregion

        #region State Queries

        private bool ShouldShowUI()
        {
            if (forceShowUI) return true;

            if (turnManager != null && turnManager.CurrentUnit != currentPlayer)
                currentPlayer = turnManager.CurrentUnit;

            if (!(currentPlayer is PlayerUnit)) return false;
            if (IsCurrentlyTargeting() || IsExecutingAbility() || uiHiddenForAnimation || uiHiddenForMovement)
                return false;

            // Block any transitional combat state. Both cases cause batched HandleTurnUIUpdate
            // calls (queued by UIEvents.OnAbilityUsed / OnAbilityAnimationComplete) to fire while
            // the game is not yet ready to show the UI:
            //
            // CameraTransition — the camera is physically moving to the new active unit.
            //   DelayedTurnUISetup explicitly polls this state and waits for it to end before
            //   calling SetUIVisibility directly, so blocking here does not prevent the UI from
            //   appearing — it just stops HandleTurnUIUpdate from showing it a frame too early.
            //
            // TurnEnding — an endTurnOnCast ability just fired. CombatManager sets this state
            //   BEFORE UIEvents.OnAbilityAnimationComplete so that the batched TurnUI update
            //   triggered by that event arrives while the block is already in place.
            if (combatManager != null)
            {
                var state = combatManager.currentState;
                if (state == CombatState.CameraTransition || state == CombatState.TurnEnding)
                    return false;
            }

            return !IsMoving();
        }

        private bool IsCurrentlyTargeting() =>
            (combatManager != null && combatManager.IsTargetingAbility) ||
            (jumpSystem != null && jumpSystem.IsTargetingJump);

        private bool IsExecutingAbility() =>
            combatManager != null && combatManager.IsExecutingAbility;

        private bool IsMoving() =>
            combatManager != null && combatManager.IsMoving;

        #endregion

        #region Public API

        private void SetUIVisibility(bool visible)
        {
            if (turnUI == null) return;

            // When hiding, always act immediately — the caller already decided we should hide.
            // When showing, guard against the flags so we never show during an animation or movement.
            if (visible && (uiHiddenForAnimation || uiHiddenForMovement)) return;

            turnUI.SetActive(visible);
            if (visible && debugMode)
                Debug.Log($"UIManager: Showing UI for {currentPlayer?.name}");
        }

        public void ShowUI()
        {
            if (!uiHiddenForAnimation && !uiHiddenForMovement)
                SetUIVisibility(true);
        }

        public void HideUI() => SetUIVisibility(false);

        public void ToggleAbilities() => abilityPanel?.ToggleAbilities();

        public void ShowAbilityTooltip(int slotIndex) => abilityPanel?.ShowAbilityTooltip(slotIndex);
        public void HideAbilityTooltip() => abilityPanel?.HideAbilityTooltip();

        private void UpdateEndTurnButtonColour(Unit unit)
        {
            if (endTurnButtonColourIndicator == null) return;
            endTurnButtonColourIndicator.color = unit?.characterData?.uiColour ?? Color.white;
        }

        #endregion

        #region Debug Utilities

        /// <summary>
        /// Conditional logger that stamps every entry with frame count and timestamp.
        /// Gate it behind debugMode so it compiles away to nothing in release builds.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void TurnLifecycleLog(string message)
        {
            if (!debugMode) return;
            Debug.Log($"[UIManager] F={Time.frameCount:D6} T={Time.time:F3}s | {message}");
        }

        /// <summary>
        /// Safety-net coroutine that fires after every PlayerUnit turn starts.
        /// If the turnUI is still invisible after <timeoutSeconds>, it logs a detailed
        /// error and attempts a forced recovery. This catches any combination of race
        /// conditions that the primary fixes don't eliminate.
        ///
        /// Architectural note — Poll vs Push: every other mechanism here is Push
        /// (event → coroutine → SetActive). This watchdog is a deliberate Poll: it
        /// checks state on a timer rather than waiting for a specific signal. That
        /// makes it resilient to exactly the failure mode we're debugging: missing
        /// or misfired signals.
        /// </summary>
        private IEnumerator TurnUIWatchdog(Unit expectedUnit, float timeoutSeconds = 2.5f)
        {
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                // Happy-path exit: UI is already up.
                if (turnUI != null && turnUI.activeSelf)
                    yield break;

                // If another unit's turn has started since this watchdog launched,
                // our window has passed — bail without interfering.
                if (currentPlayer != expectedUnit)
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // --- Reached timeout ---
            bool uiIsActive = turnUI != null && turnUI.activeSelf;
            bool unitStillCurrent = currentPlayer == expectedUnit;
            bool stillPlayerUnit = expectedUnit is PlayerUnit;
            bool cameraStillMoving = cameraController != null && cameraController.IsTransitioning;
            bool inCameraState = combatManager != null && combatManager.currentState == CombatState.CameraTransition;
            bool turnIsEnding  = combatManager != null && combatManager.currentState == CombatState.TurnEnding;

            if (!uiIsActive && unitStillCurrent && stillPlayerUnit && !cameraStillMoving && !inCameraState && !turnIsEnding)
            {
                Debug.LogError(
                    $"[UIManager] TurnUIWatchdog FIRED for unit='{expectedUnit?.name}' at F={Time.frameCount} T={Time.time:F3}s.\n" +
                    $"turnUI.activeSelf={uiIsActive} | currentPlayer={currentPlayer?.name ?? "NULL"} | " +
                    $"cameraTransitioning={cameraStillMoving} | combatState={combatManager?.currentState}\n" +
                    $"Root cause: the primary Push path (HandleTurnStarted → DelayedTurnUISetup) " +
                    $"did not produce a visible UI within {timeoutSeconds}s. Forcing recovery.");

                // Attempt recovery
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
                UpdateEndTurnButtonColour(expectedUnit);
                SetUIVisibility(true);
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();

                Debug.LogWarning($"[UIManager] TurnUIWatchdog recovery complete. turnUI.activeSelf={turnUI?.activeSelf}");
            }
        }

        #endregion

        #region Editor Support

#if UNITY_EDITOR
        void OnValidate()
        {
            baseUISize = Mathf.Max(1, baseUISize);
            referenceCameraDistance = Mathf.Max(1, referenceCameraDistance);
        }

        void OnDrawGizmos()
        {
            if (currentPlayer != null && debugMode)
            {
                Vector3 uiWorldPos = currentPlayer.transform.position + Vector3.up * worldOffset.y;
                if (mainCamera != null) uiWorldPos += mainCamera.transform.right * worldOffset.x;

                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(uiWorldPos, 0.25f);
                Gizmos.color = Color.green;
                Gizmos.DrawLine(currentPlayer.transform.position, uiWorldPos);
            }
        }
#endif

        #endregion
    }
}