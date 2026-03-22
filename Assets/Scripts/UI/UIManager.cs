using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

namespace DDD.TNFY.BRAWL
{
    public class UIManager : MonoBehaviour
    {
        #region Properties

        public bool AreAbilitiesExpanded => abilitiesExpanded;
        public bool IsUIHiddenForAnimation => uiHiddenForAnimation;
        public bool IsUIHiddenForMovement => uiHiddenForMovement;

        #endregion

        #region Serialized Fields

        [Header("=== UI References ===")]
        [SerializeField] private GameObject turnUI;
        [SerializeField] private RectTransform turnUIRectTransform;
        [SerializeField] private Button endTurnButton;

        [SerializeField] private Transform turnIndicatorsParent;
        [SerializeField] private GameObject turnIndicatorPrefab;

        [Header("=== World UI Settings ===")]
        [Tooltip("Height offset above the unit in world units")]
        [SerializeField] private Vector2 worldOffset = new Vector2(0f, 0.5f);

        [Tooltip("Base size of the UI when camera is at reference distance")]
        [SerializeField] private float baseUISize = 40f;

        [Tooltip("Reference camera distance for base UI size")]
        [SerializeField] private float referenceCameraDistance = 10f;

        [Tooltip("If true, UI maintains constant screen size regardless of camera distance")]
        [SerializeField] private bool maintainScreenSize = false;

        [Header("=== Ability Panel ===")]
        [SerializeField] private Button expandAbilitiesButton;
        [SerializeField] private GameObject abilitiesPanel;
        [SerializeField] private Button[] abilityButtons = new Button[3];
        [SerializeField] private TextMeshProUGUI abilityTooltipText;

        [Header("=== Debug ===")]
        [SerializeField] private bool debugMode = false;
        [SerializeField] private bool forceShowUI = false;

        #endregion

        #region Private Fields

        // Core system references
        private TurnManager turnManager;
        private CombatManager combatManager;
        private JumpSystem jumpSystem;
        private Camera mainCamera;
        private Canvas parentCanvas;

        // State tracking
        private Unit currentPlayer;
        private bool abilitiesExpanded = false;
        private bool uiHiddenForAnimation = false;
        private bool uiHiddenForMovement = false;

        // Turn order tracking
        private List<GameObject> turnIndicatorObjects = new List<GameObject>();
        private int currentTurnIndex = -1;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            ValidateReferences();
            SetupButtonListeners();
        }

        void Start()
        {
            CacheComponents();
            FindGameSystems();
            SubscribeToEvents();

            // Initialize UI state
            SetAbilitiesExpanded(false);
            SetUIVisibility(false);
        }

        /// <summary>
        /// Updates UI position and scale after camera movement completes
        /// </summary>
        void LateUpdate()
        {
            if (turnUI.activeInHierarchy && currentPlayer != null)
            {
                UpdateUIPositionAndScale();
            }
        }

        void OnDestroy()
        {
            CleanupButtonListeners();
            UnsubscribeFromEvents();
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Validates required UI references and sets up fallbacks
        /// </summary>
        private void ValidateReferences()
        {
            if (turnUI == null)
            {
                Debug.LogError("UIManager: turnUI reference is not set!");
                return;
            }

            if (turnUIRectTransform == null)
            {
                turnUIRectTransform = turnUI.GetComponent<RectTransform>();
            }
        }

        /// <summary>
        /// Caches frequently used components for performance
        /// </summary>
        private void CacheComponents()
        {
            mainCamera = Camera.main;

            if (turnUIRectTransform != null)
            {
                parentCanvas = turnUIRectTransform.GetComponentInParent<Canvas>();
            }
        }

        /// <summary>
        /// Finds and references game system components
        /// </summary>
        private void FindGameSystems()
        {
            turnManager = FindObjectOfType<TurnManager>();
            combatManager = FindObjectOfType<CombatManager>();
            jumpSystem = FindObjectOfType<JumpSystem>();
        }

        /// <summary>
        /// Sets up button click listeners with proper closure handling
        /// </summary>
        private void SetupButtonListeners()
        {
            if (expandAbilitiesButton != null)
            {
                expandAbilitiesButton.onClick.AddListener(ExpandAbilities);
            }

            // Capture slot index in closure for each button
            for (int i = 0; i < abilityButtons.Length; i++)
            {
                int slotIndex = i; // Important: capture by value for closure
                if (abilityButtons[i] != null)
                {
                    abilityButtons[i].onClick.AddListener(() => OnAbilityButtonClicked(slotIndex));
                }
            }
        }

        /// <summary>
        /// Cleans up button listeners to prevent memory leaks
        /// </summary>
        private void CleanupButtonListeners()
        {
            if (expandAbilitiesButton != null)
                expandAbilitiesButton.onClick.RemoveAllListeners();

            foreach (var button in abilityButtons)
            {
                if (button != null)
                    button.onClick.RemoveAllListeners();
            }
        }

        #endregion

        #region World UI Positioning

        /// <summary>
        /// Updates UI position to follow unit in world space and scales based on camera distance
        /// </summary>
        private void UpdateUIPositionAndScale()
        {
            if (currentPlayer == null || mainCamera == null || turnUIRectTransform == null || parentCanvas == null)
                return;

            // Calculate world position with offset
            Vector3 worldPosition = currentPlayer.transform.position
                + Vector3.up * worldOffset.y
                + Vector3.right * worldOffset.x;

            // Convert to viewport coordinates (0-1 normalized screen space)
            Vector3 viewportPosition = mainCamera.WorldToViewportPoint(worldPosition);

            // Hide UI if unit is behind camera
            if (viewportPosition.z <= 0)
            {
                SetUIVisibility(false);
                return;
            }

            // Convert viewport to canvas position
            RectTransform canvasRect = parentCanvas.GetComponent<RectTransform>();
            Vector2 canvasSize = canvasRect.sizeDelta;

            // Map viewport (0-1) to canvas coordinates (centered at origin)
            Vector2 canvasPosition = new Vector2(
                (viewportPosition.x - 0.5f) * canvasSize.x,
                (viewportPosition.y - 0.5f) * canvasSize.y
            );

            turnUIRectTransform.anchoredPosition = canvasPosition;

            // Apply distance-based scaling if enabled
            if (!maintainScreenSize)
            {
                float distanceToCamera = Vector3.Distance(mainCamera.transform.position, worldPosition);
                float scaleFactor = referenceCameraDistance / distanceToCamera;
                float finalScale = (baseUISize / 100f) * scaleFactor;

                turnUIRectTransform.localScale = Vector3.one * finalScale;
            }
            else
            {
                // Maintain constant screen size
                turnUIRectTransform.localScale = Vector3.one * (baseUISize / 100f);
            }
        }

        #endregion

        #region Event Management

        /// <summary>
        /// Subscribes to all relevant game events
        /// </summary>
        private void SubscribeToEvents()
        {
            // UI system events
            UIUpdateSystem.OnTurnUIUpdate += HandleTurnUIUpdate;
            UIUpdateSystem.OnAbilityButtonsUpdate += HandleAbilityButtonsUpdate;
            UIUpdateSystem.OnAbilityAnimationStarted += HandleAbilityAnimationStarted;
            UIUpdateSystem.OnAbilityAnimationComplete += HandleAbilityAnimationComplete;
            UIUpdateSystem.OnMovementAnimationStarted += HandleMovementAnimationStarted;
            UIUpdateSystem.OnMovementAnimationComplete += HandleMovementAnimationComplete;

            // Input events
            InputManager.OnEscapePressedHighPriority += HandleEscapePressed;
            InputManager.OnMouseRightClickedHighPriority += HandleRightClicked;

            // Turn management events
            TurnManager.OnTurnStarted += HandleTurnStarted;

            TurnManager.OnTurnNumberChanged += HandleTurnIndexChanged;
        }

        /// <summary>
        /// Unsubscribes from all events to prevent null reference exceptions
        /// </summary>
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
                InputManager.OnEscapePressedHighPriority -= HandleEscapePressed;
                InputManager.OnMouseRightClickedHighPriority -= HandleRightClicked;
            }

            TurnManager.OnTurnStarted -= HandleTurnStarted;

            TurnManager.OnTurnNumberChanged -= HandleTurnIndexChanged;
        }

        #endregion

        #region Event Handlers

        /// <summary>
        /// Handles turn start event and updates current player reference
        /// </summary>
        private void HandleTurnStarted(Unit unit)
        {
            currentPlayer = unit;

            // For player units, delay UI setup to ensure CombatManager is ready
            if (unit is PlayerUnit)
            {
                // Always collapse abilities when starting a new player turn
                if (abilitiesExpanded)
                {
                    SetAbilitiesExpanded(false);
                }

                // Add a small delay to ensure CombatManager has processed the turn start
                StartCoroutine(DelayedTurnUISetup());
            }
            else
            {
                // Hide UI for enemy turns and ensure end turn button is hidden
                SetUIVisibility(false);
                if (endTurnButton != null)
                {
                    endTurnButton.gameObject.SetActive(false);
                }
            }

            if (turnIndicatorObjects.Count == 0)
            {
                InitializeTurnOrderUI();
            }
            else
            {
                UpdateTurnIndicatorStates();
            }

            if (debugMode)
                Debug.Log($"Turn started for: {unit?.name}, Unit Type: {unit?.GetType()?.Name}");
        }

        /// <summary>
        /// Handles UI update requests and manages visibility based on game state
        /// </summary>
        private void HandleTurnUIUpdate()
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;

            if (ShouldShowUI())
            {
                // Ensure end turn button state is correct when showing UI
                if (endTurnButton != null)
                {
                    endTurnButton.gameObject.SetActive(currentPlayer is PlayerUnit);
                }

                SetUIVisibility(true);
                UpdateAbilityDisplay();
                UpdateAbilityButtonStates();
            }
            else
            {
                SetUIVisibility(false);
                if (abilitiesExpanded)
                {
                    SetAbilitiesExpanded(false);
                }
            }
        }

        private IEnumerator DelayedTurnUISetup()
        {
            // Wait for next frame to ensure CombatManager has set up
            yield return null;

            // Wait for camera transition if it's happening
            while (combatManager != null && combatManager.currentState == CombatState.CameraTransition)
            {
                yield return null;
            }

            // Now set up UI for player units
            if (currentPlayer is PlayerUnit)
            {
                // Set end turn button visibility BEFORE showing UI
                if (endTurnButton != null)
                {
                    endTurnButton.gameObject.SetActive(true);
                }

                SetUIVisibility(true);
                UpdateAbilityDisplay();
                UpdateAbilityButtonStates();

                if (debugMode)
                    Debug.Log($"UI setup complete for player: {currentPlayer?.name}, End Turn Button Active: {endTurnButton?.gameObject.activeSelf}");
            }
        }

        /// <summary>
        /// Updates ability button interactability when combat state changes
        /// </summary>
        private void HandleAbilityButtonsUpdate()
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;

            if (turnUI.activeInHierarchy && abilitiesExpanded)
            {
                UpdateAbilityButtonStates();
            }
        }

        /// <summary>
        /// Hides UI when ability animations start to avoid visual clutter
        /// </summary>
        private void HandleAbilityAnimationStarted()
        {
            uiHiddenForAnimation = true;
            SetUIVisibility(false);

            if (abilitiesExpanded)
            {
                SetAbilitiesExpanded(false);
            }
        }

        /// <summary>
        /// Shows UI when ability animations complete if conditions are met
        /// </summary>
        private void HandleAbilityAnimationComplete()
        {
            uiHiddenForAnimation = false;

            if (ShouldShowUI())
            {
                SetUIVisibility(true);
                UpdateAbilityDisplay();
                UpdateAbilityButtonStates();
            }
        }

        /// <summary>
        /// Hides UI during movement animations for cleaner visual presentation
        /// </summary>
        private void HandleMovementAnimationStarted()
        {
            uiHiddenForMovement = true;
            SetUIVisibility(false);

            if (abilitiesExpanded)
            {
                SetAbilitiesExpanded(false);
            }

            if (debugMode)
                Debug.Log("UI hidden for movement animation");
        }

        /// <summary>
        /// Restores UI visibility after movement animations complete
        /// </summary>
        private void HandleMovementAnimationComplete()
        {
            uiHiddenForMovement = false;

            if (ShouldShowUI())
            {
                SetUIVisibility(true);
                UpdateAbilityDisplay();
                UpdateAbilityButtonStates();
            }

            if (debugMode)
                Debug.Log("UI shown after movement animation complete");
        }

        /// <summary>
        /// Handles escape key input to collapse abilities when appropriate
        /// </summary>
        private void HandleEscapePressed()
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;

            if (abilitiesExpanded && !IsCurrentlyTargeting())
            {
                CollapseAbilities();
            }
        }

        /// <summary>
        /// Handles right-click input to collapse abilities when not targeting
        /// </summary>
        private void HandleRightClicked(Vector3 mouseWorldPosition)
        {
            if (uiHiddenForAnimation || uiHiddenForMovement) return;

            if (abilitiesExpanded && !IsCurrentlyTargeting())
            {
                CollapseAbilities();
            }
        }

        #endregion

        #region State Management

        /// <summary>
        /// Determines if UI should be visible based on current game state
        /// </summary>
        private bool ShouldShowUI()
        {
            if (forceShowUI) return true;

            // Sync current player with turn manager
            if (turnManager != null && turnManager.CurrentUnit != currentPlayer)
            {
                currentPlayer = turnManager.CurrentUnit;
            }

            // Only show UI for player units, never for enemy units
            bool isPlayerUnit = currentPlayer is PlayerUnit;

            // Don't check IsMoving() during camera transitions - it might be confused
            bool canShow = isPlayerUnit &&
                           !IsCurrentlyTargeting() &&
                           !IsExecutingAbility() &&
                           !uiHiddenForAnimation &&
                           !uiHiddenForMovement;

            // Only check movement if we're not in a transition state
            if (canShow && combatManager != null && combatManager.currentState != CombatState.CameraTransition)
            {
                canShow = !IsMoving();
            }

            return canShow;
        }

        /// <summary>
        /// Checks if any targeting system is currently active
        /// </summary>
        private bool IsCurrentlyTargeting()
        {
            bool isTargetingAbility = combatManager != null && combatManager.IsTargetingAbility;
            bool isTargetingJump = jumpSystem != null && jumpSystem.IsTargetingJump;
            return isTargetingAbility || isTargetingJump;
        }

        /// <summary>
        /// Checks if an ability is currently being executed
        /// </summary>
        private bool IsExecutingAbility()
        {
            return combatManager != null && combatManager.IsExecutingAbility;
        }

        /// <summary>
        /// Checks if a unit is currently moving
        /// </summary>
        private bool IsMoving()
        {
            return combatManager != null && combatManager.IsMoving;
        }

        /// <summary>
        /// Controls UI visibility with proper state checking
        /// </summary>
        private void SetUIVisibility(bool visible)
        {
            if (turnUI != null && !uiHiddenForAnimation && !uiHiddenForMovement)
            {
                turnUI.SetActive(visible);

                if (visible && debugMode)
                {
                    Debug.Log($"Showing UI for: {currentPlayer?.name}");
                }
            }
        }

        #endregion

        #region Ability System

        /// <summary>
        /// Expands the abilities panel if conditions allow
        /// </summary>
        private void ExpandAbilities()
        {
            if (!abilitiesExpanded && !uiHiddenForAnimation && !uiHiddenForMovement)
            {
                SetAbilitiesExpanded(true);
                UpdateAbilityDisplay();
                UpdateAbilityButtonStates();
            }
        }

        /// <summary>
        /// Collapses the abilities panel and hides tooltips
        /// </summary>
        private void CollapseAbilities()
        {
            if (abilitiesExpanded)
            {
                SetAbilitiesExpanded(false);
                HideAbilityTooltip();
            }
        }

        /// <summary>
        /// Sets the expanded state and toggles appropriate UI elements
        /// </summary>
        private void SetAbilitiesExpanded(bool expanded)
        {
            abilitiesExpanded = expanded;

            if (expandAbilitiesButton != null)
                expandAbilitiesButton.gameObject.SetActive(!expanded);

            if (abilitiesPanel != null)
                abilitiesPanel.SetActive(expanded);
        }

        /// <summary>
        /// Handles ability button clicks and initiates targeting
        /// </summary>
        private void OnAbilityButtonClicked(int slotIndex)
        {
            if (combatManager == null || currentPlayer == null) return;
            if (!combatManager.CanUseAbility) return;

            var abilities = currentPlayer.characterData.abilityLoadout;
            if (slotIndex < 0 || slotIndex >= abilities.Length) return;
            if (abilities[slotIndex] == null) return;

            SetAbilitiesExpanded(false);
            combatManager.EnterAbilityTargeting(slotIndex);
        }

        /// <summary>
        /// Updates ability button sprites and visibility based on current player's loadout
        /// </summary>
        private void UpdateAbilityDisplay()
        {
            if (currentPlayer?.characterData == null) return;

            var abilities = currentPlayer.characterData.abilityLoadout;

            // Update expand button interactability
            if (expandAbilitiesButton != null)
            {
                bool hasAnyAbilities = System.Array.Exists(abilities, a => a != null);
                expandAbilitiesButton.interactable = hasAnyAbilities && !uiHiddenForAnimation && !uiHiddenForMovement;
            }

            // Update individual ability buttons when panel is expanded
            if (abilitiesExpanded)
            {
                for (int i = 0; i < abilityButtons.Length; i++)
                {
                    if (abilityButtons[i] == null) continue;

                    if (i < abilities.Length && abilities[i] != null)
                    {
                        var ability = abilities[i];
                        Image buttonImage = abilityButtons[i].GetComponent<Image>();
                        if (buttonImage != null)
                        {
                            buttonImage.sprite = ability.image;
                        }
                        abilityButtons[i].gameObject.SetActive(true);
                    }
                    else
                    {
                        abilityButtons[i].gameObject.SetActive(false);
                    }
                }

                HideAbilityTooltip();
            }
        }

        /// <summary>
        /// Updates ability button interactability based on combat manager state
        /// </summary>
        private void UpdateAbilityButtonStates()
        {
            if (combatManager == null || currentPlayer?.characterData == null) return;

            bool canUseAbility = combatManager.CanUseAbility && !uiHiddenForAnimation && !uiHiddenForMovement;

            if (expandAbilitiesButton != null)
            {
                expandAbilitiesButton.interactable = canUseAbility;
            }
        }

        #endregion

        #region Tooltips

        /// <summary>
        /// Displays tooltip information for the specified ability slot
        /// </summary>
        public void ShowAbilityTooltip(int slotIndex)
        {
            if (currentPlayer?.characterData == null || abilityTooltipText == null) return;

            var abilities = currentPlayer.characterData.abilityLoadout;
            if (slotIndex < 0 || slotIndex >= abilities.Length) return;

            var ability = abilities[slotIndex];
            if (ability == null) return;

            abilityTooltipText.text = $"{ability.abilityName}\n{ability.description}\nDamage: {ability.damage}\nRange: {ability.range}";
        }

        /// <summary>
        /// Clears the ability tooltip display
        /// </summary>
        public void HideAbilityTooltip()
        {
            if (abilityTooltipText != null)
            {
                abilityTooltipText.text = "";
            }
        }

        #endregion

        #region Turn Order UI Management

        /// <summary>
        /// Initializes turn order indicators when turn order is established
        /// </summary>
        private void InitializeTurnOrderUI()
        {
            if (turnManager == null || turnIndicatorsParent == null || turnIndicatorPrefab == null)
                return;

            ClearTurnIndicators();
            CreateTurnIndicators();
            UpdateTurnIndicatorStates();
        }

        /// <summary>
        /// Creates turn indicator UI objects for each unit in turn order
        /// </summary>
        private void CreateTurnIndicators()
        {
            var turnOrder = turnManager.TurnOrder;

            for (int i = 0; i < turnOrder.Count; i++)
            {
                var unit = turnOrder[i];
                GameObject indicatorObj = Instantiate(turnIndicatorPrefab, turnIndicatorsParent);

                SetupTurnIndicator(indicatorObj, unit, i);
                turnIndicatorObjects.Add(indicatorObj);
            }
        }

        /// <summary>
        /// Sets up individual turn indicator with unit data and button functionality
        /// </summary>
        private void SetupTurnIndicator(GameObject indicatorObj, Unit unit, int turnIndex)
        {
            // Get components from the prefab
            Button button = indicatorObj.GetComponent<Button>();
            Image portraitImage = indicatorObj.transform.Find("Portrait")?.GetComponent<Image>();

            // Set up portrait from character data
            if (portraitImage != null && unit != null)
            {
                SetUnitPortrait(portraitImage, unit);
            }

            // Set up button click functionality
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnTurnIndicatorClicked(turnIndex, unit));
            }
        }

        /// <summary>
        /// Sets the portrait image from unit's character data
        /// </summary>
        private void SetUnitPortrait(Image portraitImage, Unit unit)
        {
            // Try to get portrait from character data
            if (unit.characterData != null && unit.characterData.portrait != null)
            {
                portraitImage.sprite = unit.characterData.portrait;
            }
            else
            {
                // Fallback: try to get sprite from unit's renderer
                var spriteRenderer = unit.GetComponentInChildren<SpriteRenderer>();
                if (spriteRenderer != null && spriteRenderer.sprite != null)
                {
                    portraitImage.sprite = spriteRenderer.sprite;
                }
            }
        }

        /// <summary>
        /// Handles turn indicator button clicks for camera transitions
        /// </summary>
        private void OnTurnIndicatorClicked(int turnIndex, Unit targetUnit)
        {
            if (!CanTransitionCamera() || targetUnit == null)
                return;

            TransitionCameraToUnit(targetUnit);
        }

        /// <summary>
        /// Checks if camera transition is allowed based on current game state
        /// </summary>
        private bool CanTransitionCamera()
        {
            // Don't allow transitions during animations or movement
            if (uiHiddenForAnimation || uiHiddenForMovement)
                return false;

            // Check combat manager state - allow during camera transitions but not other blocking states
            if (combatManager != null)
            {
                var state = combatManager.currentState;
                return state == CombatState.WaitingForInput ||
                       state == CombatState.CameraTransition;
            }

            return true;
        }

        /// <summary>
        /// Transitions camera to focus on the specified unit with smooth movement and proper offset
        /// </summary>
        private void TransitionCameraToUnit(Unit targetUnit)
        {
            if (targetUnit?.currentTile == null)
                return;

            // Calculate target position with offset
            Vector3 targetPosition = new Vector3(
                targetUnit.transform.position.x,
                targetUnit.transform.position.y + 2f,
                targetUnit.transform.position.z - 4f
            );

            // Start smooth camera transition
            StartCoroutine(SmoothCameraTransition(targetPosition));
        }

        /// <summary>
        /// Smoothly transitions camera to target position over 1 second
        /// </summary>
        private System.Collections.IEnumerator SmoothCameraTransition(Vector3 targetPosition)
        {
            var cameraController = FindObjectOfType<CameraController>();
            if (cameraController == null)
                yield break;

            // Temporarily disable button interactability during transition
            SetTurnIndicatorButtonsInteractable(false);

            Camera mainCam = Camera.main;
            if (mainCam == null)
                yield break;

            Vector3 startPosition = mainCam.transform.position;

            // Apply bounds checking to target position if camera controller has bounds
            targetPosition = cameraController.ClampToBounds(targetPosition);

            float transitionDuration = 1f;
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / transitionDuration;

                // Use smooth step for eased transition
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                mainCam.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);

                yield return null;
            }

            // Ensure exact final position
            mainCam.transform.position = targetPosition;

            // Re-enable button interactability after transition
            SetTurnIndicatorButtonsInteractable(true);
        }

        /// <summary>
        /// Sets interactability state for all turn indicator buttons
        /// </summary>
        private void SetTurnIndicatorButtonsInteractable(bool interactable)
        {
            foreach (var indicatorObj in turnIndicatorObjects)
            {
                if (indicatorObj != null)
                {
                    Button button = indicatorObj.GetComponent<Button>();
                    if (button != null)
                    {
                        button.interactable = interactable && CanTransitionCamera();
                    }
                }
            }
        }

        /// <summary>
        /// Updates visual states of all turn indicators based on current turn
        /// </summary>
        private void UpdateTurnIndicatorStates()
        {
            if (turnManager == null)
                return;

            currentTurnIndex = turnManager.CurrentTurnIndex;

            for (int i = 0; i < turnIndicatorObjects.Count; i++)
            {
                var indicatorObj = turnIndicatorObjects[i];
                if (indicatorObj != null)
                {
                    UpdateSingleIndicatorState(indicatorObj, i == currentTurnIndex);
                }
            }
        }

        /// <summary>
        /// Updates visual state of a single turn indicator
        /// </summary>
        private void UpdateSingleIndicatorState(GameObject indicatorObj, bool isActive)
        {
            // Update button interactability - check if we can transition
            Button button = indicatorObj.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = CanTransitionCamera();
            }

            // Update visual highlighting for active turn
            Image backgroundImage = indicatorObj.GetComponent<Image>();
            if (backgroundImage != null)
            {
                backgroundImage.color = isActive ? Color.green : Color.white;
            }

            // Optional: Update active indicator object if it exists
            Transform activeIndicator = indicatorObj.transform.Find("ActiveIndicator");
            if (activeIndicator != null)
            {
                activeIndicator.gameObject.SetActive(isActive);
            }
        }

        /// <summary>
        /// Clears all existing turn indicator objects
        /// </summary>
        private void ClearTurnIndicators()
        {
            foreach (var indicatorObj in turnIndicatorObjects)
            {
                if (indicatorObj != null)
                {
                    DestroyImmediate(indicatorObj);
                }
            }
            turnIndicatorObjects.Clear();
        }

        /// <summary>
        /// Handles turn index changes to update indicator states
        /// </summary>
        private void HandleTurnIndexChanged(int totalTurns, int currentIndex)
        {
            UpdateTurnIndicatorStates();
        }

        #endregion

        #region Public API

        /// <summary>
        /// Forces UI to show if not blocked by animations
        /// </summary>
        public void ShowUI()
        {
            if (!uiHiddenForAnimation && !uiHiddenForMovement)
            {
                SetUIVisibility(true);
            }
        }

        /// <summary>
        /// Forces UI to hide regardless of state
        /// </summary>
        public void HideUI()
        {
            SetUIVisibility(false);
        }

        /// <summary>
        /// Toggles abilities panel expansion state
        /// </summary>
        public void ToggleAbilities()
        {
            if (!uiHiddenForAnimation && !uiHiddenForMovement)
            {
                if (abilitiesExpanded)
                    CollapseAbilities();
                else
                    ExpandAbilities();
            }
        }

        #endregion

        #region Editor Support

#if UNITY_EDITOR
        /// <summary>
        /// Validates inspector values to ensure they remain within sensible ranges
        /// </summary>
        void OnValidate()
        {
            baseUISize = Mathf.Max(1, baseUISize);
            referenceCameraDistance = Mathf.Max(1, referenceCameraDistance);
        }

        /// <summary>
        /// Draws debug gizmos showing UI world position and connection to unit
        /// </summary>
        void OnDrawGizmos()
        {
            if (currentPlayer != null && debugMode)
            {
                // Calculate UI world position with offsets
                Vector3 uiWorldPos = currentPlayer.transform.position + Vector3.up * worldOffset.y;

                // Apply X offset in camera right direction if available
                if (mainCamera != null)
                {
                    uiWorldPos += mainCamera.transform.right * worldOffset.x;
                }

                // Draw UI position indicator
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireSphere(uiWorldPos, 0.25f);

                // Draw connection line from unit to UI position
                Gizmos.color = Color.green;
                Gizmos.DrawLine(currentPlayer.transform.position, uiWorldPos);
            }
        }

        /// <summary>
        /// Debug context menu to log current UI positioning settings
        /// </summary>
        [ContextMenu("Log Current Settings")]
        private void LogCurrentSettings()
        {
            if (mainCamera != null && currentPlayer != null)
            {
                Vector3 cameraRight = mainCamera.transform.right;
                Vector3 worldPos = currentPlayer.transform.position
                    + Vector3.up * worldOffset.y
                    + cameraRight * worldOffset.x;
                float distance = Vector3.Distance(mainCamera.transform.position, worldPos);

                Debug.Log($"Camera Distance: {distance:F2}");
                Debug.Log($"Current Scale Factor: {referenceCameraDistance / distance:F2}");
                Debug.Log($"UI World Position: {worldPos}");
                Debug.Log($"World Offset: {worldOffset}");
            }
        }
#endif

        #endregion
    }
}