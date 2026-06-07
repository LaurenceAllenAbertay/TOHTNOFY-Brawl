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
            currentPlayer = unit;
            turnOrderController?.HandleTurnStarted(unit);

            if (unit is PlayerUnit)
            {
                abilityPanel?.HandleTurnStarted();
                StartCoroutine(DelayedTurnUISetup());
            }
            else
            {
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

            yield return null;
            while (combatManager != null && combatManager.currentState == CombatState.CameraTransition)
                yield return null;

            if (unitForThisTurn is PlayerUnit)
            {
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
                SetUIVisibility(true);
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();
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
                    endTurnButton.gameObject.SetActive(currentPlayer is PlayerUnit);
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
                // Only suppress the UI if the camera is settled. During a transition the
                // unit may briefly fall behind the frustum — hiding it here would fight
                // DelayedTurnUISetup and leave the UI permanently hidden.
                if (combatManager == null || combatManager.currentState != CombatState.CameraTransition)
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

            bool isPlayerUnit = currentPlayer is PlayerUnit;
            bool canShow = isPlayerUnit && !IsCurrentlyTargeting() &&
                           !IsExecutingAbility() && !uiHiddenForAnimation && !uiHiddenForMovement;

            if (canShow && combatManager != null && combatManager.currentState != CombatState.CameraTransition)
                canShow = !IsMoving();

            return canShow;
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