using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum CombatState
    {
        WaitingForInput,
        ExecutingAction,
        MovingUnit,
        TurnEnding,
        CameraTransition
    }

    public class CombatManager : MonoBehaviour
    {
        public static CombatManager Instance { get; private set; }

        #region Properties

        public bool CanMove => currentState == CombatState.WaitingForInput &&
                               !_movementLockedByAbility && !isWaitingForAnimation && !isMoving &&
                               GetRemainingMovement() > 0;
        public bool CanUseAbility => currentState == CombatState.WaitingForInput &&
                                     !hasUsedAbilityThisTurn && !isWaitingForAnimation && !isMoving &&
                                     (currentActiveUnit == null || currentActiveUnit.CanUseAbilities());
        public bool IsTargetingAbility => targetingController != null && targetingController.IsTargetingAbility;
        public bool IsExecutingAbility => isWaitingForAnimation;
        public bool IsMoving => isMoving;
        public bool IsExecutingPendingAction => isExecutingPendingAction;
        public bool IsBlockingAllInput => isBlockingAllInput;
        public bool HasMovedThisTurn => _hasMovedThisTurn;
        public bool HasUsedAbilityThisTurn => hasUsedAbilityThisTurn;
        public bool IsMovementLocked => _movementLockedByAbility;
        public Unit CurrentActiveUnit => currentActiveUnit;

        public bool CanEndTurn => currentState == CombatState.WaitingForInput &&
                                  !isMoving && !isWaitingForAnimation &&
                                  currentActiveUnit is PlayerUnit &&
                                  (Time.time - turnStartTime >= turnStartProtectionDuration);

        public bool IsSafeForAdminCommand
        {
            get
            {
                if (currentState != CombatState.WaitingForInput) return false;
                if (isMoving || isWaitingForAnimation) return false;
                if (!(currentActiveUnit is PlayerUnit)) return false;
                if (IsTargetingAbility) return false;
                if (jumpSystem != null && jumpSystem.IsTargetingJump) return false;
                if (cameraController != null && cameraController.IsTransitioning) return false;

                foreach (var unit in UnitManager.AllUnits)
                {
                    var unitAnimator = unit.GetComponent<UnitAnimator>();
                    if (unitAnimator != null && unitAnimator.IsInKnockbackSequence) return false;
                }

                return true;
            }
        }

        public int GetRemainingMovement() => Mathf.Max(0, totalMovementPoints - movementPointsUsed);
        public int GetTotalMovement() => totalMovementPoints;
        public int GetUsedMovement() => movementPointsUsed;
        public bool HasMovementRemaining() => GetRemainingMovement() > 0;
        public bool HasEnoughMovementForJump(int jumpRange) => GetRemainingMovement() >= jumpRange;

        #endregion

        #region Serialized Fields

        [Header("State")]
        public CombatState currentState = CombatState.WaitingForInput;

        [Header("Debug")]
        [SerializeField] private bool debugMode = false;

        #endregion

        #region Private Fields

        private TurnManager turnManager;
        private JumpSystem jumpSystem;
        private CameraController cameraController;
        private AbilityTargetingController targetingController;

        private Unit currentActiveUnit;
        private bool hasUsedAbilityThisTurn;

        private int movementPointsUsed;
        private int totalMovementPoints;

        private bool isWaitingForAnimation;
        private bool isMoving;
        private bool isBlockingAllInput;
        private bool isExecutingPendingAction;

        private float turnStartTime;
        private const float turnStartProtectionDuration = 1.5f;
        private bool _turnTransitionPending;
        private bool _hasMovedThisTurn;
        private bool _movementLockedByAbility;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            InitializeComponents();
            SubscribeToEvents();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();

            if (Instance == this)
                Instance = null;
        }

        #endregion

        #region Initialization

        private void InitializeComponents()
        {
            turnManager = FindAnyObjectByType<TurnManager>();
            jumpSystem = FindAnyObjectByType<JumpSystem>();
            cameraController = FindAnyObjectByType<CameraController>();
            targetingController = GetComponent<AbilityTargetingController>();
        }

        private void SubscribeToEvents()
        {
            Tile.OnTileClicked += HandleTileClicked;
            Tile.OnTileHovered += HandleTileHovered;
            Tile.OnTileHoverExited += HandleTileHoverExited;
            TurnManager.OnTurnStarted += OnTurnStarted;
            InputManager.OnMouseMoved += HandleMouseMoved;
            InputManager.OnMouseClicked += HandleMouseClicked;
            InputManager.OnMouseRightClicked += HandleMouseRightClicked;
            InputManager.OnAbilitySelected += HandleAbilitySelected;
            InputManager.OnEscapePressed += HandleEscapePressed;
            InputManager.OnEndTurnRequested += EndTurn;
        }

        private void UnsubscribeFromEvents()
        {
            Tile.OnTileClicked -= HandleTileClicked;
            Tile.OnTileHovered -= HandleTileHovered;
            Tile.OnTileHoverExited -= HandleTileHoverExited;
            TurnManager.OnTurnStarted -= OnTurnStarted;

            if (InputManager.Instance != null)
            {
                InputManager.OnMouseMoved -= HandleMouseMoved;
                InputManager.OnMouseClicked -= HandleMouseClicked;
                InputManager.OnMouseRightClicked -= HandleMouseRightClicked;
                InputManager.OnAbilitySelected -= HandleAbilitySelected;
                InputManager.OnEscapePressed -= HandleEscapePressed;
                InputManager.OnEndTurnRequested -= EndTurn;
            }
        }

        #endregion

        #region Turn Management

        public void OnTurnStarted(Unit activeUnit)
        {
            currentActiveUnit = activeUnit;
            ResetTurnState();
            NotifyAllTargetingTurnStarted();
            turnStartTime = Time.time;
            movementPointsUsed = 0;
            totalMovementPoints = activeUnit.currentSpeed;

            if (cameraController != null)
            {
                currentState = CombatState.CameraTransition;
                StartCoroutine(WaitForCameraTransition());
            }
            else
            {
                SetupTurnAfterCameraTransition();
            }
        }
        
        private void NotifyAllTargetingTurnStarted()
        {
            foreach (var unit in UnitManager.AllUnits)
            {
                foreach (var ability in UnitLoadoutManager.GetAbilities(unit))
                    ability?.targeting?.ResetForNewTurn();
            }
        }
        
        private void NotifyCurrentUnitTargetingTurnStarted()
        {
            if (currentActiveUnit == null) return;
            foreach (var ability in UnitLoadoutManager.GetAbilities(currentActiveUnit))
                ability?.targeting?.ResetForNewTurn();
        }

        private void ResetTurnState()
        {
            hasUsedAbilityThisTurn = false;
            _hasMovedThisTurn = false;
            _movementLockedByAbility = false;
            isWaitingForAnimation = false;
            isMoving = false;
            _turnTransitionPending = false;
        }

        private IEnumerator WaitForCameraTransition()
        {
            if (debugMode)
                Debug.Log($"[CombatManager] F={Time.frameCount:D6} T={Time.time:F3}s | " +
                          $"WaitForCameraTransition START — yielding one frame before polling IsTransitioning");

            yield return null;

            int waitFrames = 0;
            while (cameraController != null && cameraController.IsTransitioning)
            {
                waitFrames++;
                if (debugMode && waitFrames % 10 == 0)
                    Debug.Log($"[CombatManager] F={Time.frameCount:D6} T={Time.time:F3}s | " +
                              $"WaitForCameraTransition still waiting — frame {waitFrames} into poll");
                yield return null;
            }

            SetupTurnAfterCameraTransition();
        }

        private void SetupTurnAfterCameraTransition()
        {
            _turnTransitionPending = false;
            targetingController?.ClearAbilityTargeting();
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            
            if (currentActiveUnit.pendingAction.HasValue)
            {
                if (!currentActiveUnit.CanUseAbilities())
                {
                    currentActiveUnit.pendingAction = null;
                    Debug.Log($"[CombatManager] {currentActiveUnit.name} is scared — pending action cleared.");
                }
                else
                {
                    var pending = currentActiveUnit.pendingAction.Value;
                    currentActiveUnit.pendingAction = null;
                    currentState = CombatState.ExecutingAction;
                    var ctx = new AbilityContext
                    {
                        caster = currentActiveUnit,
                        ability = pending.ability,
                        aimDir = pending.aimDir
                    };
                    StartCoroutine(ExecutePendingAction(ctx));
                    return;
                }
            }

            currentState = CombatState.WaitingForInput;

            if (currentActiveUnit is PlayerUnit)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    currentActiveUnit,
                    movementRangeOverride: GetRemainingMovement());
                UIUpdateSystem.ForceImmediateUpdate(
                    UIUpdateSystem.UIUpdateFlags.TurnUI | UIUpdateSystem.UIUpdateFlags.AbilityButtons);
            }

            UIEvents.OnActiveUnitChanged();
        }

        private IEnumerator ExecutePendingAction(AbilityContext ctx)
        {
            isExecutingPendingAction = true;

            yield return StartCoroutine(
                ExecuteAbilityWithAnimation(ctx.ability, ctx, isDirectional: true, ctx.aimDir));

            isExecutingPendingAction = false;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            _turnTransitionPending = true;
            currentState = CombatState.TurnEnding;
            turnManager.EndTurn();
        }

        public void EndTurn()
        {
            if (!(currentActiveUnit is PlayerUnit)) return;
            if (_turnTransitionPending) return;
            if (Time.time - turnStartTime < turnStartProtectionDuration) return;
            if (isMoving || currentState == CombatState.MovingUnit) return;
            if (isWaitingForAnimation || currentState == CombatState.ExecutingAction) return;
            if (currentState != CombatState.WaitingForInput) return;

            _turnTransitionPending = true;
            currentState = CombatState.TurnEnding;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            targetingController?.CancelAbilityTargeting(currentActiveUnit);
            turnManager.EndTurn();
        }

        #endregion

        #region Input Routing

        private bool ShouldBlockInput()
        {
            if (isBlockingAllInput) return true;
            if (currentState != CombatState.WaitingForInput) return true;
            if (currentActiveUnit == null) return true;
            if (!(currentActiveUnit is PlayerUnit)) return true;
            if (isWaitingForAnimation || isMoving) return true;

            var unitAnimator = currentActiveUnit.GetComponent<UnitAnimator>();
            if (unitAnimator != null && unitAnimator.IsInKnockbackSequence) return true;

            if (cameraController != null && cameraController.IsTransitioning) return true;
            return false;
        }

        private void HandleMouseMoved(Vector3 mouseWorldPosition)
        {
            if (ShouldBlockInput()) return;
            targetingController?.HandleMouseMoved(mouseWorldPosition, currentActiveUnit);
        }

        private void HandleMouseClicked(Vector3 mouseWorldPosition)
        {
            if (ShouldBlockInput()) return;
            if (IsTargetingAbility && !(targetingController.CurrentAbility?.targeting is SingleTargeting))
                targetingController.HandleMouseClicked(mouseWorldPosition, currentActiveUnit);
        }

        private void HandleMouseRightClicked(Vector3 mouseWorldPosition)
        {
            if (ShouldBlockInput()) return;
            if (IsTargetingAbility)
                targetingController.CancelAbilityTargeting(currentActiveUnit);
        }

        private void HandleAbilitySelected(int slot)
        {
            if (ShouldBlockInput()) return;
            if (CanUseAbility && !IsTargetingAbility)
                EnterAbilityTargeting(slot);
        }

        private void HandleEscapePressed()
        {
            if (ShouldBlockInput()) return;
            if (IsTargetingAbility)
                targetingController.CancelAbilityTargeting(currentActiveUnit);
        }

        private void HandleTileClicked(Tile clickedTile)
        {
            if (ShouldBlockInput()) return;
            if (InputManager.IsMouseOverUI_Static()) return;
            if (jumpSystem != null && jumpSystem.IsTargetingJump) return;

            if (IsTargetingAbility)
            {
                targetingController.HandleTileClicked(clickedTile, currentActiveUnit);
                return;
            }

            if (CanMove)
                AttemptMovement(clickedTile);
        }

        private void HandleTileHovered(Tile hoveredTile)
        {
            if (ShouldBlockInput()) return;
            if (InputManager.IsMouseOverUI_Static()) return;
            if (jumpSystem != null && jumpSystem.IsTargetingJump) return;

            if (IsTargetingAbility)
                targetingController.HandleTileHovered(hoveredTile, currentActiveUnit);
        }

        private void HandleTileHoverExited(Tile exitedTile)
        {
            if (ShouldBlockInput()) return;
            if (InputManager.IsMouseOverUI_Static()) return;
            if (jumpSystem != null && jumpSystem.IsTargetingJump) return;

            if (IsTargetingAbility)
                targetingController.HandleTileHoverExited(exitedTile, currentActiveUnit);
        }

        #endregion

        #region Ability System — Public API

        public void EnterAbilityTargeting(int slot)
        {
            if (ShouldBlockInput()) return;
            if (hasUsedAbilityThisTurn) return;
            
            var abilities = UnitLoadoutManager.GetAbilities(currentActiveUnit);
            if (slot >= 0 && slot < abilities.Length && currentActiveUnit != null &&
                currentActiveUnit.IsAbilityOnCooldown(slot)) return;

            if (jumpSystem != null && jumpSystem.IsTargetingJump)
                jumpSystem.CancelJumpTargeting();

            targetingController?.EnterAbilityTargeting(slot, currentActiveUnit);
        }

        public void CancelAbilityTargeting()
        {
            targetingController?.CancelAbilityTargeting(currentActiveUnit);
        }
        
        public void StartAbilityExecution(Ability ability, AbilityContext ctx, bool isDirectional, Vector2Int aimDir)
        {
            StartCoroutine(ExecuteAbilityWithAnimation(ability, ctx, isDirectional, aimDir));
        }

        #endregion

        #region Ability Execution

        private IEnumerator ExecuteAbilityWithAnimation(Ability ability, AbilityContext ctx, bool isDirectional, Vector2Int aimDir)
        {
            currentState = CombatState.ExecutingAction;
            isWaitingForAnimation = true;
            BlockAllInput();
            UIEvents.OnAbilityAnimationStarted();

            bool isRandomAOE = (ability?.targeting?.UsesCameraZoomAfterExecution == true) && cameraController != null;

            bool executionSuccessful;
            if (isDirectional)
                executionSuccessful = ability.Execute(currentActiveUnit, aimDir);
            else if (ctx != null)
                executionSuccessful = ability.ExecuteWithContext(ctx);
            else
                executionSuccessful = ability.Execute(currentActiveUnit, Vector2Int.up);

            if (executionSuccessful)
            {
                hasUsedAbilityThisTurn = true;
                if (_hasMovedThisTurn)
                    _movementLockedByAbility = true;
                
                yield return StartCoroutine(WaitForCompleteAbilitySequence(ability));

                if (isRandomAOE)
                {
                    Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();
                    float worldRadius = ability.range * Mathf.Max(tileSpacing.x, tileSpacing.z);
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.FitRadius(currentActiveUnit.transform.position, worldRadius)));
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(currentActiveUnit)));
                }

                if (currentActiveUnit != null && currentActiveUnit.IsDead)
                {
                    UnblockAllInput();
                    isWaitingForAnimation = false;
                    _turnTransitionPending = true;
                    currentState = CombatState.TurnEnding;
                    UIEvents.OnAbilityAnimationComplete();
                    UIEvents.OnTargetingStateChanged();
                    GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                    turnManager.EndTurn();
                    yield break;
                }
                
                UnblockAllInput();
                isWaitingForAnimation = false;
                currentState = CombatState.WaitingForInput;

                targetingController?.RestoreDefaultHighlights(
                    currentActiveUnit,
                    suppressMovement: ability.endTurnOnCast || isExecutingPendingAction);
                targetingController?.ClearAbilityTargeting();
                UIEvents.OnAbilityUsed();
            }
            else
            {
                targetingController?.ShowRetryPreview(currentActiveUnit);
            }
            
            UnblockAllInput();
            isWaitingForAnimation = false;
            currentState = CombatState.WaitingForInput;

            if (ability.endTurnOnCast && !isExecutingPendingAction)
            {
                _turnTransitionPending = true;
                currentState = CombatState.TurnEnding;
            }

            UIEvents.OnAbilityAnimationComplete();
            UIEvents.OnTargetingStateChanged();

            if (ability.endTurnOnCast && !isExecutingPendingAction)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                turnManager.EndTurn();
            }
        }

        private IEnumerator WaitForCompleteAbilitySequence(Ability ability)
        {
            var animator = currentActiveUnit.GetComponent<UnitAnimator>()?.GetComponent<Animator>();

            if (animator != null && !string.IsNullOrEmpty(ability.AnimationState))
            {
                yield return new WaitForSeconds(0.1f);

                float maxWaitTime = 30f;
                float elapsed = 0f;

                while (elapsed < maxWaitTime)
                {
                    bool cameraTransitioning = cameraController != null && cameraController.IsTransitioning;
                    bool abilityExecuting = currentActiveUnit.currentAbilityContext != null;
                    
                    bool anyKnockbackPlaying = false;
                    foreach (var unit in UnitManager.AllUnits)
                    {
                        var ua = unit.GetComponent<UnitAnimator>();
                        if (ua != null && ua.IsInKnockbackSequence)
                        {
                            anyKnockbackPlaying = true;
                            break;
                        }
                    }

                    if (!cameraTransitioning && !abilityExecuting && !anyKnockbackPlaying) break;

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                yield return new WaitForSeconds(0.2f);
            }
            else
            {
                yield return new WaitForSeconds(0.5f);
            }
        }

        #endregion

        #region Movement System

        private void AttemptMovement(Tile targetTile)
        {
            var waypoints = GridManager.Instance.FindPathOptimized(
                currentActiveUnit.currentTile, targetTile, GetRemainingMovement());

            if (waypoints.Count == 0) return;

            var fullPath = GridManager.Instance.FindPath(
                currentActiveUnit.currentTile, targetTile, GetRemainingMovement());

            movementPointsUsed += fullPath.Count;

            StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                currentActiveUnit,
                targetTile,
                waypoints,
                followCameraForAI: false,
                onMovementStarted: () =>
                {
                    currentState = CombatState.MovingUnit;
                    isMoving = true;
                    UIEvents.OnMovementAnimationStarted();
                    GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                },
                onMovementComplete: () =>
                {
                    _hasMovedThisTurn = true;
                    UIEvents.OnMovementAnimationComplete();
                    UIEvents.OnUnitMoved();
                    UIEvents.OnCombatStateChanged();
                    isMoving = false;
                    currentState = CombatState.WaitingForInput;

                    NotifyCurrentUnitTargetingTurnStarted();

                    if (currentActiveUnit is PlayerUnit && GetRemainingMovement() > 0)
                    {
                        GridManager.Instance.SetHighlightMode(
                            GridManager.HighlightMode.Movement,
                            currentActiveUnit,
                            movementRangeOverride: GetRemainingMovement());
                    }
                }
            ));
        }
        
        public void SetMovementUsed()
        {
            movementPointsUsed = totalMovementPoints;
            _hasMovedThisTurn = true;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            UIEvents.OnCombatStateChanged();
        }

        #endregion

        #region Input Blocking

        public void BlockAllInput() => isBlockingAllInput = true;
        public void UnblockAllInput() => isBlockingAllInput = false;
        
        public void BlockAnimationForEffect()
        {
            currentState = CombatState.ExecutingAction;
            isWaitingForAnimation = true;
        }

        public void ReleaseAnimationBlock()
        {
            isWaitingForAnimation = false;
            currentState = CombatState.WaitingForInput;
        }

        #endregion
    }
}