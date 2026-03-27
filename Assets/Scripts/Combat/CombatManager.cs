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
        #region Properties

        public bool CanMove => currentState == CombatState.WaitingForInput &&
                               !hasMovedThisTurn && !isWaitingForAnimation && !isMoving;
        public bool CanUseAbility => currentState == CombatState.WaitingForInput &&
                                     !hasUsedAbilityThisTurn && !isWaitingForAnimation && !isMoving;
        public bool IsTargetingAbility => targetingController != null && targetingController.IsTargetingAbility;
        public bool IsExecutingAbility => isWaitingForAnimation;
        public bool IsMoving => isMoving;
        public bool IsBlockingAllInput => isBlockingAllInput;
        public bool HasMovedThisTurn => hasMovedThisTurn;
        public Unit CurrentActiveUnit => currentActiveUnit;

        public bool CanEndTurn => currentState == CombatState.WaitingForInput &&
                                  !isMoving && !isWaitingForAnimation &&
                                  currentActiveUnit is PlayerUnit &&
                                  (Time.time - turnStartTime >= turnStartProtectionDuration);

        public int GetRemainingMovement() => Mathf.Max(0, totalMovementPoints - movementPointsUsed);
        public int GetTotalMovement() => totalMovementPoints;
        public int GetUsedMovement() => movementPointsUsed;
        public bool HasMovementRemaining() => GetRemainingMovement() > 0;
        public bool HasEnoughMovementForJump(int jumpRange) => GetRemainingMovement() >= jumpRange;

        #endregion

        #region Serialized Fields

        [Header("State")]
        public CombatState currentState = CombatState.WaitingForInput;

        #endregion

        #region Private Fields

        private TurnManager turnManager;
        private JumpSystem jumpSystem;
        private CameraController cameraController;
        private AbilityTargetingController targetingController;

        private Unit currentActiveUnit;
        private bool hasMovedThisTurn;
        private bool hasUsedAbilityThisTurn;

        private int movementPointsUsed;
        private int totalMovementPoints;

        private bool isWaitingForAnimation;
        private bool isMoving;
        private bool isBlockingAllInput;

        private float turnStartTime;
        private const float turnStartProtectionDuration = 1.5f;
        private bool _turnTransitionPending;

        #endregion

        #region Unity Lifecycle

        void Start()
        {
            InitializeComponents();
            SubscribeToEvents();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
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
            turnStartTime = Time.time;
            movementPointsUsed = 0;
            totalMovementPoints = activeUnit.currentSpeed;

            if (cameraController != null)
            {
                currentState = CombatState.CameraTransition;
                StartCoroutine(WaitForCameraTransition());
            }
        }

        private void ResetTurnState()
        {
            hasMovedThisTurn = false;
            hasUsedAbilityThisTurn = false;
            isWaitingForAnimation = false;
            isMoving = false;
        }

        private IEnumerator WaitForCameraTransition()
        {
            while (cameraController != null && cameraController.IsTransitioning)
                yield return null;
            yield return null;
            SetupTurnAfterCameraTransition();
        }

        private void SetupTurnAfterCameraTransition()
        {
            _turnTransitionPending = false;
            currentState = CombatState.WaitingForInput;
            targetingController?.ClearAbilityTargeting();
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            if (currentActiveUnit is PlayerUnit)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, currentActiveUnit);
                UIUpdateSystem.ForceImmediateUpdate(
                    UIUpdateSystem.UIUpdateFlags.TurnUI | UIUpdateSystem.UIUpdateFlags.AbilityButtons);
            }

            UIEvents.OnActiveUnitChanged();
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

            if (!hasMovedThisTurn)
                AttemptMovement(clickedTile);
        }

        #endregion

        #region Ability System — Public API

        public void EnterAbilityTargeting(int slot)
        {
            if (ShouldBlockInput()) return;
            if (hasUsedAbilityThisTurn) return;
            targetingController?.EnterAbilityTargeting(slot, currentActiveUnit);
        }

        public void CancelAbilityTargeting()
        {
            targetingController?.CancelAbilityTargeting(currentActiveUnit);
        }

        /// <summary>
        /// Called by AbilityTargetingController when the player has confirmed an ability.
        /// Owns execution-state flags and waits for the full animation sequence.
        /// </summary>
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

            if (ability?.targeting is RandomAOETargeting randomTargeting)
                randomTargeting.OnAbilityExecuted();

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
                yield return StartCoroutine(WaitForCompleteAbilitySequence(ability));
                targetingController?.RestoreDefaultHighlights(currentActiveUnit);
                targetingController?.ClearAbilityTargeting();
                UIEvents.OnAbilityUsed();
            }
            else
            {
                targetingController?.ShowRetryPreview(currentActiveUnit);
            }

            UIEvents.OnAbilityAnimationComplete();
            UIEvents.OnTargetingStateChanged();
            UnblockAllInput();
            isWaitingForAnimation = false;
            currentState = CombatState.WaitingForInput;
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

                    if (!cameraTransitioning && !abilityExecuting) break;

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                yield return new WaitForSeconds(0.2f);
            }
            else
            {
                yield return new WaitForSeconds(5f);
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
            hasMovedThisTurn = true;

            StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                currentActiveUnit,
                targetTile,
                waypoints,
                followCamera: true,
                onMovementStarted: () =>
                {
                    currentState = CombatState.MovingUnit;
                    isMoving = true;
                    UIEvents.OnMovementAnimationStarted();
                    GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                },
                onMovementComplete: () =>
                {
                    UIEvents.OnMovementAnimationComplete();
                    UIEvents.OnUnitMoved();
                    UIEvents.OnCombatStateChanged();
                    isMoving = false;
                    currentState = CombatState.WaitingForInput;
                }
            ));
        }

        /// <summary>
        /// Marks all movement as consumed. Called by JumpSystem and movement-consuming abilities.
        /// </summary>
        public void SetMovementUsed()
        {
            movementPointsUsed = totalMovementPoints;
            hasMovedThisTurn = true;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            UIEvents.OnCombatStateChanged();
        }

        #endregion

        #region Input Blocking

        public void BlockAllInput() => isBlockingAllInput = true;
        public void UnblockAllInput() => isBlockingAllInput = false;

        #endregion
    }
}