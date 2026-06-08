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
        public bool IsExecutingPendingAction => isExecutingPendingAction;
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
        private bool hasMovedThisTurn;

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

        /// <summary>
        /// Notifies every ability's targeting system that a new turn has started.
        /// Targeting types that cache per-turn state (e.g. RandomAOETargeting) override
        /// ResetForNewTurn to refresh themselves; the base implementation is a no-op.
        /// Called at the start of every turn so cached selections are always fresh.
        /// </summary>
        private void NotifyAllTargetingTurnStarted()
        {
            foreach (var unit in UnitManager.AllUnits)
            {
                foreach (var ability in UnitLoadoutManager.GetAbilities(unit))
                    ability?.targeting?.ResetForNewTurn();
            }
        }

        /// <summary>
        /// Notifies only the active unit's targeting systems that a turn event occurred.
        /// Called after the active unit moves so cached selections re-roll relative to
        /// their new position.
        /// </summary>
        private void NotifyCurrentUnitTargetingTurnStarted()
        {
            if (currentActiveUnit == null) return;
            foreach (var ability in UnitLoadoutManager.GetAbilities(currentActiveUnit))
                ability?.targeting?.ResetForNewTurn();
        }

        private void ResetTurnState()
        {
            hasUsedAbilityThisTurn = false;
            hasMovedThisTurn = false;
            isWaitingForAnimation = false;
            isMoving = false;
            _turnTransitionPending = false;
        }

        private IEnumerator WaitForCameraTransition()
        {
            // Yield one frame before checking IsTransitioning.
            // CameraController.OnTurnStarted starts its TransitionTo coroutine
            // synchronously (sets isTransitioning = true up to its first yield),
            // BUT only if its handler fires before this coroutine's first check.
            // If Script Execution Order causes CombatManager to subscribe before
            // CameraController, isTransitioning would still be false here without
            // this guard — causing SetupTurnAfterCameraTransition to run early
            // while the camera is still physically moving. The one-frame yield
            // ensures CameraController has always had a chance to start its
            // transition before we poll the flag.
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

            if (debugMode)
                Debug.Log($"[CombatManager] F={Time.frameCount:D6} T={Time.time:F3}s | " +
                          $"WaitForCameraTransition DONE — waited {waitFrames} poll-frames, calling SetupTurnAfterCameraTransition");

            SetupTurnAfterCameraTransition();
        }

        private void SetupTurnAfterCameraTransition()
        {
            _turnTransitionPending = false;
            targetingController?.ClearAbilityTargeting();
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Check for a queued follow-up action before giving control to the player.
            if (currentActiveUnit.pendingAction.HasValue)
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

            // Yield directly on the ability coroutine so we block until it fully completes,
            // including all animations and knockback. No external state polling needed.
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

                // Wait for the ability animation to fully complete before moving the camera.
                yield return StartCoroutine(WaitForCompleteAbilitySequence(ability));

                // After the animation, zoom out to frame the full range so the player can
                // see where all the hits landed, then smoothly return to the caster.
                if (isRandomAOE)
                {
                    Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();
                    float worldRadius = ability.range * Mathf.Max(tileSpacing.x, tileSpacing.z);
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.FitRadius(currentActiveUnit.transform.position, worldRadius)));
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(currentActiveUnit)));
                }

                // Clear the animation block BEFORE restoring highlights so that CanMove
                // evaluates correctly inside RestoreDefaultHighlights. Previously these
                // were set after the call, which meant CanMove was always false at the
                // point highlights were restored — causing movement tiles to never reappear
                // after abilities that don't end the turn (e.g. Double Prong).
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

            // State already cleared above on the success path; clean up here only for
            // the failed-execution path where the block above was skipped.
            UnblockAllInput();
            isWaitingForAnimation = false;
            currentState = CombatState.WaitingForInput;

            // If the ability demands an immediate turn end, mark TurnEnding NOW — before
            // firing UIEvents.OnAbilityAnimationComplete. UIManager's HandleAbilityAnimationComplete
            // calls ShouldShowUI(), which would return true for a PlayerUnit in WaitingForInput.
            // By moving to TurnEnding first, ShouldShowUI() sees the correct state and keeps
            // the UI hidden, preventing a one-frame flash before EndTurn fires OnTurnStarted.
            if (ability.endTurnOnCast && !isExecutingPendingAction)
            {
                _turnTransitionPending = true;
                currentState = CombatState.TurnEnding;
            }

            UIEvents.OnAbilityAnimationComplete();
            UIEvents.OnTargetingStateChanged();

            // If the ability demands an immediate turn end (player-controlled cast only).
            // ExecutePendingAction handles its own turn end after yielding on this coroutine.
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

                    // Also wait for any knockback animations on target units to fully complete.
                    // Knockback runs as a coroutine on the target, not the caster, so
                    // currentAbilityContext clearing does not mean knockback is done.
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
                    UIEvents.OnMovementAnimationComplete();
                    UIEvents.OnUnitMoved();
                    UIEvents.OnCombatStateChanged();
                    isMoving = false;
                    currentState = CombatState.WaitingForInput;

                    // Re-roll targeting selections relative to the unit's new position.
                    NotifyCurrentUnitTargetingTurnStarted();

                    // Restore movement highlights if the player still has points left
                    // and hasn't used an ability yet this turn.
                    if (currentActiveUnit is PlayerUnit && GetRemainingMovement() > 0 && !hasUsedAbilityThisTurn)
                    {
                        GridManager.Instance.SetHighlightMode(
                            GridManager.HighlightMode.Movement,
                            currentActiveUnit,
                            movementRangeOverride: GetRemainingMovement());
                    }
                }
            ));
        }

        /// <summary>
        /// Marks all movement as consumed. Called by JumpSystem and movement-consuming abilities.
        /// </summary>
        public void SetMovementUsed()
        {
            movementPointsUsed = totalMovementPoints;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            UIEvents.OnCombatStateChanged();
        }

        #endregion

        #region Input Blocking

        public void BlockAllInput() => isBlockingAllInput = true;
        public void UnblockAllInput() => isBlockingAllInput = false;

        /// <summary>
        /// Called by effects that manage their own animation timing (e.g. self-knockback,
        /// movement effect, charge displacement) to acquire the animation block without going
        /// through the full ExecuteAbilityWithAnimation flow.
        /// Always pair with a corresponding ReleaseAnimationBlock call.
        /// </summary>
        public void BlockAnimationForEffect()
        {
            currentState = CombatState.ExecutingAction;
            isWaitingForAnimation = true;
        }

        /// <summary>
        /// Paired release for BlockAnimationForEffect. Clears the animation block and
        /// returns the manager to WaitingForInput so the player can act again.
        /// </summary>
        public void ReleaseAnimationBlock()
        {
            isWaitingForAnimation = false;
            currentState = CombatState.WaitingForInput;
        }

        #endregion
    }
}