using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class UIManager : MonoBehaviour
    {
        public bool AreAbilitiesExpanded => abilityPanel != null && abilityPanel.AreAbilitiesExpanded;
        public bool IsUIHiddenForAnimation => uiHiddenForAnimation;
        public bool IsUIHiddenForMovement => uiHiddenForMovement;

        [Header("HUD Root")]
        [SerializeField] private GameObject turnUI;
        [SerializeField] private RectTransform turnUIRectTransform;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Image endTurnButtonColourIndicator;

        [Header("World UI Settings")]
        [SerializeField] private Vector2 worldOffset = new Vector2(0f, 0.5f);
        [SerializeField] private float baseUISize = 40f;
        [SerializeField] private float referenceCameraDistance = 10f;
        [SerializeField] private bool maintainScreenSize = false;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;
        [SerializeField] private bool forceShowUI = false;

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

        private void HandleTurnStarted(Unit unit)
        {
            currentPlayer = unit;
            turnOrderController?.HandleTurnStarted(unit);

            if (unit is PlayerUnit)
            {

                if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
                SetUIVisibility(false);

                abilityPanel?.HandleTurnStarted();
                StartCoroutine(DelayedTurnUISetup());
                StartCoroutine(TurnUIWatchdog(unit));
            }
            else
            {
                SetUIVisibility(false);
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(false);
            }
        }

        private IEnumerator DelayedTurnUISetup()
        {
            Unit unitForThisTurn = currentPlayer;

            yield return null;

            int waitFrames = 0;
            while (combatManager != null && combatManager.currentState == CombatState.CameraTransition)
            {
                waitFrames++;

                yield return null;
            }

            if (unitForThisTurn is PlayerUnit)
            {
                if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
                UpdateEndTurnButtonColour(unitForThisTurn);
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

        private bool ShouldShowUI()
        {
            if (forceShowUI) return true;

            if (turnManager != null && turnManager.CurrentUnit != currentPlayer)
                currentPlayer = turnManager.CurrentUnit;

            if (!(currentPlayer is PlayerUnit)) return false;
            if (IsCurrentlyTargeting() || IsExecutingAbility() || uiHiddenForAnimation || uiHiddenForMovement)
                return false;

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
        
        private void SetUIVisibility(bool visible)
        {
            if (turnUI == null) return;

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

        private IEnumerator TurnUIWatchdog(Unit expectedUnit, float timeoutSeconds = 2.5f)
        {
            float elapsed = 0f;

            while (elapsed < timeoutSeconds)
            {
                if (turnUI != null && turnUI.activeSelf)
                    yield break;

                if (currentPlayer != expectedUnit)
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }

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

                if (endTurnButton != null) endTurnButton.gameObject.SetActive(true);
                UpdateEndTurnButtonColour(expectedUnit);
                SetUIVisibility(true);
                abilityPanel?.UpdateAbilityDisplay();
                abilityPanel?.UpdateAbilityButtonStates();

                Debug.LogWarning($"[UIManager] TurnUIWatchdog recovery complete. turnUI.activeSelf={turnUI?.activeSelf}");
            }
        }
    }
}