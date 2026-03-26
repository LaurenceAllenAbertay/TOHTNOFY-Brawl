using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DDD.TNFY.BRAWL
{
    public enum CombatState
    {
        WaitingForInput,    // Player can move or use abilities
        ExecutingAction,    // Animation/effect playing
        MovingUnit,         // Unit is currently moving
        TurnEnding,         // Cleanup before next turn
        CameraTransition    // Camera is moving to focus on new unit
    }

    public class CombatManager : MonoBehaviour
    {
        #region Properties

        // Combat state properties - core game state queries
        public bool CanMove => currentState == CombatState.WaitingForInput &&
                                !hasMovedThisTurn &&
                                !isWaitingForAnimation &&
                                !isMoving;
        public bool CanUseAbility => currentState == CombatState.WaitingForInput &&
                                !hasUsedAbilityThisTurn &&
                                !isWaitingForAnimation &&
                                !isMoving;
        public bool IsTargetingAbility => currentAbility != null;
        public bool IsExecutingAbility => isWaitingForAnimation;
        public bool IsMoving => isMoving;
        public Unit CurrentActiveUnit => currentActiveUnit;

        // Add this field to the private fields section
        private bool isBlockingAllInput = false;

        // Add this property to the properties section
        public bool IsBlockingAllInput => isBlockingAllInput;

        /// <summary>
        /// Indicates whether the player can end their turn based on current state
        /// </summary>
        public bool CanEndTurn => currentState == CombatState.WaitingForInput &&
                                 !isMoving &&
                                 !isWaitingForAnimation &&
                                 currentActiveUnit is PlayerUnit &&
                                 (Time.time - turnStartTime >= turnStartProtectionDuration);

        // Movement tracking properties - essential for UI and game logic
        public int GetRemainingMovement() => Mathf.Max(0, totalMovementPoints - movementPointsUsed); 
        public int GetTotalMovement() => totalMovementPoints;
        public int GetUsedMovement() => movementPointsUsed;
        public bool HasMovementRemaining() => GetRemainingMovement() > 0;
        public bool HasEnoughMovementForJump(int jumpRange) => GetRemainingMovement() >= jumpRange;

        #endregion

        #region Serialized Fields

        [Header("State")]
        public CombatState currentState = CombatState.WaitingForInput;

        [Header("Movement Settings")]
        [SerializeField] private float movementSpeed = 4f; // Units per second

        #endregion

        #region Private Fields

        // Core system references
        private TurnManager turnManager;
        private JumpSystem jumpSystem;
        private CameraController cameraController;

        // Current turn tracking
        private Unit currentActiveUnit;
        private bool hasMovedThisTurn = false;
        private bool hasUsedAbilityThisTurn = false;

        // Movement system state
        private int movementPointsUsed = 0;
        private int totalMovementPoints = 0;

        // Ability targeting state
        private Ability currentAbility;
        private int currentAbilitySlot;
        private Tile hoveredTile; // For single targeting preview

        // Animation coordination flags
        private bool isWaitingForAnimation = false;
        private bool isMoving = false;

        // Turn protection timing
        private float turnStartTime;
        private const float turnStartProtectionDuration = 1.5f;

        // Prevents end-turn spam: locked from the moment EndTurn fires until
        // the next turn is fully set up and ready for player input.
        private bool _turnTransitionPending = false;

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

        /// <summary>
        /// Finds and caches references to core game systems
        /// </summary>
        private void InitializeComponents()
        {
            turnManager = FindAnyObjectByType<TurnManager>();
            jumpSystem = FindAnyObjectByType<JumpSystem>();
            cameraController = FindAnyObjectByType<CameraController>();
        }

        /// <summary>
        /// Subscribes to all input and tile interaction events
        /// </summary>
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

        /// <summary>
        /// Safely unsubscribes from all events to prevent null reference exceptions
        /// </summary>
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

        /// <summary>
        /// Initializes a new turn for the specified unit, handling camera transitions and state setup
        /// </summary>
        public void OnTurnStarted(Unit activeUnit)
        {
            currentActiveUnit = activeUnit;
            ResetTurnState();

            // Record turn start time for protection period
            turnStartTime = Time.time;

            // Initialize movement system for this turn
            movementPointsUsed = 0;
            totalMovementPoints = activeUnit.currentSpeed;

            // Handle camera transition
            if (cameraController != null)
            {
                currentState = CombatState.CameraTransition;
                StartCoroutine(WaitForCameraTransition());
            }
        }

        /// <summary>
        /// Resets all turn-based flags and states for a fresh turn
        /// </summary>
        private void ResetTurnState()
        {
            hasMovedThisTurn = false;
            hasUsedAbilityThisTurn = false;
            isWaitingForAnimation = false;
            isMoving = false;
        }

        /// <summary>
        /// Waits for camera transition to complete before allowing player input
        /// </summary>
        private IEnumerator WaitForCameraTransition()
        {
            // Poll camera transition state
            while (cameraController != null && cameraController.IsTransitioning)
            {
                yield return null;
            }

            // Small buffer to ensure camera has fully settled
            yield return null;

            SetupTurnAfterCameraTransition();
        }

        /// <summary>
        /// Completes turn setup after camera transition, configures UI and highlights
        /// </summary>
        private void SetupTurnAfterCameraTransition()
        {
            // Release the end-turn spam lock now that the next turn is fully ready
            _turnTransitionPending = false;

            currentState = CombatState.WaitingForInput;

            // Clear any lingering targeting state from previous turns
            ClearAbilityTargeting();

            // Always clear highlights first to avoid stale visuals
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Show movement highlights only for player units (AI doesn't need visual feedback)
            if (currentActiveUnit is PlayerUnit)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, currentActiveUnit);

                // FIXED: Force UI to update for player units AND ensure end turn button is accessible
                UIUpdateSystem.ForceImmediateUpdate(UIUpdateSystem.UIUpdateFlags.TurnUI |
                                                   UIUpdateSystem.UIUpdateFlags.AbilityButtons);
            }

            // Notify UI systems that turn is ready
            UIEvents.OnActiveUnitChanged();
        }

        /// <summary>
        /// Ends the current turn and transitions to next unit
        /// Prevents ending turn during movement, camera transitions, or within protection period
        /// </summary>
        public void EndTurn()
        {
            // Only the player may manually end a turn — AI ends its own turn internally
            if (!(currentActiveUnit is PlayerUnit))
                return;

            // Block all further end-turn requests until the next turn is fully ready
            if (_turnTransitionPending)
                return;

            // Prevent ending turn within first 2 seconds of turn start
            if (Time.time - turnStartTime < turnStartProtectionDuration)
                return;

            // Prevent ending turn during active movement
            if (isMoving || currentState == CombatState.MovingUnit)
                return;

            // Prevent during ability execution
            if (isWaitingForAnimation || currentState == CombatState.ExecutingAction)
                return;

            // Only allow ending turn when in waiting for input state
            if (currentState != CombatState.WaitingForInput)
            {
                Debug.Log("Cannot end turn in current state");
                return;
            }

            _turnTransitionPending = true;
            currentState = CombatState.TurnEnding;
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            CancelAbilityTargeting();
            turnManager.EndTurn();
        }

        #endregion

        #region Input System

        /// <summary>
        /// Determines if input should be blocked based on current game state
        /// Critical for preventing input during animations and transitions
        /// </summary>
        private bool ShouldBlockInput()
        {
            // Block ALL input if explicitly set
            if (isBlockingAllInput) return true;

            // Block during non-input states
            if (currentState != CombatState.WaitingForInput) return true;

            // Block if no active unit
            if (currentActiveUnit == null) return true;

            // Block during animations or movement
            if (isWaitingForAnimation || isMoving) return true;

            // Block during knockback animations by checking UnitAnimator state
            if (currentActiveUnit != null)
            {
                var unitAnimator = currentActiveUnit.GetComponent<UnitAnimator>();
                if (unitAnimator != null && unitAnimator.IsInKnockbackSequence)
                {
                    return true;
                }
            }

            // Block during camera transitions for smooth UX
            if (cameraController != null && cameraController.IsTransitioning) return true;

            return false;
        }

        /// <summary>
        /// Handles mouse movement for ability targeting previews and hover effects
        /// </summary>
        private void HandleMouseMoved(Vector3 mouseWorldPosition)
        {
            if (ShouldBlockInput()) return;

            // Only process movement during ability targeting
            if (IsTargetingAbility)
            {
                if (currentAbility.targeting is SingleTargeting)
                {
                    HandleSingleTargetingMouseMove(mouseWorldPosition);
                }
                else if (currentAbility.targeting is MultiTileSelectionTargeting)
                {
                    HandleMultiTileSelectionMouseMove(mouseWorldPosition);
                }
                else
                {
                    HandleDirectionalTargetingMouseMove(mouseWorldPosition);
                }
            }
        }

        /// <summary>
        /// Handles primary mouse clicks for movement and ability confirmation
        /// </summary>
        private void HandleMouseClicked(Vector3 mouseWorldPosition)
        {
            if (ShouldBlockInput()) return;

            // Tile-based interactions are handled by HandleTileClicked
            if (IsTargetingAbility && currentAbility.targeting is SingleTargeting)
            {
                // Single targeting uses tile click events
                return;
            }
            else if (IsTargetingAbility)
            {
                // Directional targeting uses world position clicks
                HandleDirectionalAbilityClick(mouseWorldPosition);
            }
        }

        /// <summary>
        /// Handles right-click for canceling actions and returning to default state
        /// </summary>
        private void HandleMouseRightClicked(Vector3 mouseWorldPosition)
        {
            if (ShouldBlockInput()) return;

            if (IsTargetingAbility)
            {
                CancelAbilityTargeting();
            }
        }

        /// <summary>
        /// Handles keyboard shortcuts for quick ability access (1, 2, 3 keys)
        /// </summary>
        private void HandleAbilitySelected(int slot)
        {
            if (ShouldBlockInput()) return;

            if (CanUseAbility && !IsTargetingAbility)
            {
                EnterAbilityTargeting(slot);
            }
        }

        /// <summary>
        /// Handles escape key for canceling current actions
        /// </summary>
        private void HandleEscapePressed()
        {
            if (ShouldBlockInput()) return;

            if (IsTargetingAbility)
            {
                CancelAbilityTargeting();
            }
        }

        /// <summary>
        /// Handles tile click events for movement and single-target abilities
        /// Central hub for tile-based interactions
        /// </summary>
        private void HandleTileClicked(Tile clickedTile)
        {
            if (ShouldBlockInput()) return;

            // Ignore clicks when mouse is over UI elements
            if (InputManager.IsMouseOverUI_Static()) return;

            // Defer to jump system if it's active (jump takes priority)
            if (jumpSystem != null && jumpSystem.IsTargetingJump) return;

            if (IsTargetingAbility)
            {
                // Handle ability targeting
                if (currentAbility.targeting is SingleTargeting)
                {
                    ConfirmSingleTargetAbility(clickedTile);
                }
                else if (currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)
                {
                    HandleMultiTileSelectionClick(clickedTile, multiTargeting);
                }
                // Other targeting types don't use tile clicks
                return;
            }

            // Handle movement if player hasn't moved this turn
            if (!hasMovedThisTurn)
            {
                AttemptMovement(clickedTile);
            }
        }

        #endregion

        #region Movement System

        /// <summary>
        /// Attempts to move the current unit to the target tile if a valid path exists
        /// Now uses optimized pathfinding for smoother movement
        /// </summary>
        /// <summary>
        /// Attempts to move the current unit to the target tile if a valid path exists
        /// Now properly updates movement points immediately when movement is confirmed
        /// </summary>
        private void AttemptMovement(Tile targetTile)
        {
            // Get optimized path (waypoints only)
            var waypoints = GridManager.Instance.FindPathOptimized(
                currentActiveUnit.currentTile,
                targetTile,
                GetRemainingMovement()
            );

            if (waypoints.Count > 0)
            {
                // Calculate actual movement cost using full path (not just waypoints)
                var fullPath = GridManager.Instance.FindPath(
                    currentActiveUnit.currentTile,
                    targetTile,
                    GetRemainingMovement()
                );

                // Update movement points IMMEDIATELY when movement is confirmed
                movementPointsUsed += fullPath.Count;
                hasMovedThisTurn = true;

                // Start the movement animation
                StartCoroutine(ExecuteAnimatedMovement(
                    currentActiveUnit,      // movingUnit
                    targetTile,            // destination  
                    waypoints,             // waypoints
                    true                   // updatePlayerState (this is player movement)
                ));
            }
        }

        /// <summary>
        /// Executes animated movement for any unit with optional camera following
        /// Movement points are now updated before animation starts for player units
        /// </summary>
        public IEnumerator ExecuteAnimatedMovement(Unit movingUnit, Tile destination, List<Tile> waypoints = null, bool updatePlayerState = false)
        {
            if (movingUnit == null || destination == null) yield break;

            // Use waypoints if provided, otherwise create path
            List<Tile> pathToUse = waypoints;
            if (pathToUse == null || pathToUse.Count == 0)
            {
                int moveRange = movingUnit == currentActiveUnit ? GetRemainingMovement() : movingUnit.GetEffectiveMovementRange();
                pathToUse = GridManager.Instance.FindPathOptimized(movingUnit.currentTile, destination, moveRange);

                if (pathToUse.Count == 0) yield break;

                // For AI units that call this directly, update their movement state here
                if (!updatePlayerState && movingUnit == currentActiveUnit)
                {
                    var fullPath = GridManager.Instance.FindPath(movingUnit.currentTile, destination, moveRange);
                    movementPointsUsed += fullPath.Count;
                    hasMovedThisTurn = true;
                }
            }

            // Player-specific state management
            if (updatePlayerState && movingUnit == currentActiveUnit)
            {
                currentState = CombatState.MovingUnit;
                isMoving = true;
                UIEvents.OnMovementAnimationStarted();
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
            }

            // Get animation components
            var unitAnimator = movingUnit.GetComponent<UnitAnimator>();
            var spriteRenderer = movingUnit.GetComponentInChildren<SpriteRenderer>();

            // Start movement animation
            if (unitAnimator != null)
            {
                unitAnimator.PlayMove();
            }

            // NEW: Determine if camera should follow this movement
            bool shouldFollowWithCamera = movingUnit == currentActiveUnit && movingUnit is EnemyUnit;

            // Execute movement with conditional camera following
            if (shouldFollowWithCamera)
            {
                yield return StartCoroutine(MoveAlongWaypointsWithCamera(movingUnit, pathToUse, spriteRenderer));
            }
            else
            {
                yield return StartCoroutine(MoveAlongWaypoints(movingUnit, pathToUse, spriteRenderer));
            }

            // Stop movement animation
            if (unitAnimator != null)
            {
                unitAnimator.PlayIdle();
            }

            // Set final logical position
            movingUnit.SetCurrentTile(destination);

            // Only return to natural facing for player units during player movement
            if (updatePlayerState && movingUnit == currentActiveUnit)
            {
                movingUnit.ReturnToNaturalFacing();

                // Movement points already updated in AttemptMovement for player units
                UIEvents.OnMovementAnimationComplete();
                UIEvents.OnUnitMoved();
                UIEvents.OnCombatStateChanged();

                isMoving = false;
                currentState = CombatState.WaitingForInput;
            }
        }


        /// <summary>
        /// Moves unit along waypoints WITHOUT camera following
        /// </summary>
        private IEnumerator MoveAlongWaypoints(Unit movingUnit, List<Tile> waypoints, SpriteRenderer spriteRenderer)
        {
            Vector3 currentPos = movingUnit.transform.position;
            Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

            // Calculate total movement time based on total distance
            float totalDistance = 0f;
            Vector3 lastPos = currentPos;
            foreach (var waypoint in waypoints)
            {
                Vector3 waypointPos = waypoint.transform.position;
                totalDistance += Vector3.Distance(lastPos, waypointPos);
                lastPos = waypointPos;
            }
            float totalTime = totalDistance / movementSpeed;

            // Move through each waypoint segment
            foreach (var waypoint in waypoints)
            {
                Vector3 segmentStart = currentPos;
                Vector3 segmentEnd = waypoint.transform.position;
                float segmentDistance = Vector3.Distance(segmentStart, segmentEnd);
                float segmentTime = (segmentDistance / totalDistance) * totalTime;

                // Update sprite facing for this segment
                if (spriteRenderer != null)
                {
                    Vector3 direction = (segmentEnd - segmentStart).normalized;
                    if (Mathf.Abs(direction.x) > 0.1f)
                    {
                        spriteRenderer.flipX = direction.x < 0;
                    }
                }

                // Animate this segment
                float segmentElapsed = 0f;
                while (segmentElapsed < segmentTime)
                {
                    segmentElapsed += Time.deltaTime;
                    float segmentT = segmentElapsed / segmentTime;

                    // Smooth interpolation for this segment
                    float smoothSegmentT = Mathf.SmoothStep(0f, 1f, segmentT);
                    movingUnit.transform.position = Vector3.Lerp(segmentStart, segmentEnd, smoothSegmentT);

                    yield return null;
                }

                currentPos = segmentEnd;

                // Trigger any OnEnter tile effects for this waypoint tile.
                // yield return ensures animations finish before the unit continues moving.
                if (waypoint.HasActiveEffects)
                    yield return StartCoroutine(waypoint.TriggerOnEnterEffects(movingUnit));
            }

            // Ensure exact final position
            movingUnit.transform.position = waypoints[waypoints.Count - 1].transform.position;
        }
        private IEnumerator MoveAlongWaypointsWithCamera(Unit movingUnit, List<Tile> waypoints, SpriteRenderer spriteRenderer)
        {
            Vector3 currentPos = movingUnit.transform.position;
            Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

            // Calculate total movement time based on total distance
            float totalDistance = 0f;
            Vector3 lastPos = currentPos;
            foreach (var waypoint in waypoints)
            {
                Vector3 waypointPos = waypoint.transform.position;
                totalDistance += Vector3.Distance(lastPos, waypointPos);
                lastPos = waypointPos;
            }
            float totalTime = totalDistance / movementSpeed;

            // Camera setup - smooth movement to final destination
            Vector3 cameraStartPos = Vector3.zero;
            Vector3 cameraTargetPos = Vector3.zero;
            bool shouldMoveCamera = cameraController != null;

            if (shouldMoveCamera)
            {
                cameraStartPos = cameraController.transform.position;
                Vector3 finalPosition = waypoints[waypoints.Count - 1].transform.position;
                cameraTargetPos = new Vector3(
                    finalPosition.x,
                    finalPosition.y + 2f,
                    finalPosition.z - 3.5f
                );

                // Apply bounds checking
                cameraTargetPos = cameraController.ClampToBounds(cameraTargetPos);
            }

            // Track total elapsed time for smooth camera movement
            float totalElapsed = 0f;

            // Move through each waypoint segment (unit follows waypoints exactly)
            foreach (var waypoint in waypoints)
            {
                Vector3 segmentStart = currentPos;
                Vector3 segmentEnd = waypoint.transform.position;
                float segmentDistance = Vector3.Distance(segmentStart, segmentEnd);
                float segmentTime = (segmentDistance / totalDistance) * totalTime;

                // Update sprite facing for this segment
                if (spriteRenderer != null)
                {
                    Vector3 direction = (segmentEnd - segmentStart).normalized;
                    if (Mathf.Abs(direction.x) > 0.1f)
                    {
                        spriteRenderer.flipX = direction.x < 0;
                    }
                }

                // Animate this segment
                float segmentElapsed = 0f;
                while (segmentElapsed < segmentTime)
                {
                    segmentElapsed += Time.deltaTime;
                    totalElapsed += Time.deltaTime;
                    float segmentT = segmentElapsed / segmentTime;

                    // Unit moves through waypoints exactly as before
                    float smoothSegmentT = Mathf.SmoothStep(0f, 1f, segmentT);
                    movingUnit.transform.position = Vector3.Lerp(segmentStart, segmentEnd, smoothSegmentT);

                    // Camera smoothly moves to final destination based on total progress
                    if (shouldMoveCamera)
                    {
                        float totalProgress = Mathf.Clamp01(totalElapsed / totalTime);
                        float smoothCameraT = Mathf.SmoothStep(0f, 1f, totalProgress);
                        cameraController.transform.position = Vector3.Lerp(cameraStartPos, cameraTargetPos, smoothCameraT);
                    }

                    yield return null;
                }

                currentPos = segmentEnd;

                // Trigger any OnEnter tile effects for this waypoint tile.
                if (waypoint.HasActiveEffects)
                    yield return StartCoroutine(waypoint.TriggerOnEnterEffects(movingUnit));
            }

            // Ensure exact final positions
            movingUnit.transform.position = waypoints[waypoints.Count - 1].transform.position;
            if (shouldMoveCamera)
            {
                cameraController.transform.position = cameraTargetPos;
            }
        }

        private void ReturnToNaturalFacing(Unit unit)
        {
            if (unit == null) return;

            Vector2Int naturalFacing = MapManager.GetNaturalFacing(unit);

            // EnemyUnits face the opposite of natural facing
            if (unit is EnemyUnit)
            {
                if (naturalFacing == Vector2Int.left)
                    naturalFacing = Vector2Int.right;
                else if (naturalFacing == Vector2Int.right)
                    naturalFacing = Vector2Int.left;
                else if (naturalFacing == Vector2Int.up)
                    naturalFacing = Vector2Int.down;
                else if (naturalFacing == Vector2Int.down)
                    naturalFacing = Vector2Int.up;
            }

            unit.FaceDirection(naturalFacing);
        }

        /// <summary>
        /// Marks all movement as used (called by jump system and other movement-consuming abilities)
        /// </summary>
        public void SetMovementUsed()
        {
            movementPointsUsed = totalMovementPoints;
            hasMovedThisTurn = true;

            // Clear movement highlights since no movement remains
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Notify UI of state change
            UIEvents.OnCombatStateChanged();
        }

        #endregion

        #region Ability System

        /// <summary>
        /// Enters ability targeting mode for the specified slot
        /// Validates ability availability and initializes targeting UI
        /// </summary>
        public void EnterAbilityTargeting(int slot)
        {
            if (ShouldBlockInput()) return;
            if (hasUsedAbilityThisTurn) return;

            currentAbility = currentActiveUnit.characterData.abilityLoadout[slot];
            if (currentAbility == null || currentAbility.targeting == null) return;

            currentAbilitySlot = slot;

            // Clear any existing highlights
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Show immediate preview for single-target abilities
            if (currentAbility.targeting is SingleTargeting singleTargeting)
            {
                ShowSingleTargetRangePreview();
            }

            // Begin selection session for multi-tile selection abilities
            if (currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)
            {
                var ctx = new AbilityContext { caster = currentActiveUnit, ability = currentAbility };
                multiTargeting.BeginSelection(ctx);
                ShowMultiTileSelectionPreview(multiTargeting, ctx);
            }

            // Notify UI systems of targeting state change
            UIEvents.OnTargetingStateChanged();
        }

        /// <summary>
        /// Cancels ability targeting and returns to default state
        /// Handles cleanup for different targeting types
        /// </summary>
        public void CancelAbilityTargeting()
        {
            // Clear random targeting cache if applicable
            if (currentAbility?.targeting is RandomAOETargeting randomTargeting)
            {
                randomTargeting.OnTargetingCancelled();
            }

            // Clear multi-tile selection state
            if (currentAbility?.targeting is MultiTileSelectionTargeting multiTargeting)
            {
                multiTargeting.CancelSelection();
            }

            ClearAbilityTargeting();
            RestoreDefaultHighlights();
            UIEvents.OnTargetingStateChanged();
        }

        /// <summary>
        /// Clears ability targeting state without UI updates
        /// </summary>
        private void ClearAbilityTargeting()
        {
            currentAbility = null;
            hoveredTile = null;
        }

        /// <summary>
        /// Restores default highlight state based on current turn status
        /// </summary>
        private void RestoreDefaultHighlights()
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Show movement highlights only if player can still move
            if (currentActiveUnit is PlayerUnit && !hasMovedThisTurn)
            {
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, currentActiveUnit);
            }
        }

        /// <summary>
        /// Validates whether an ability can be executed with current parameters
        /// Used to prevent failed execution attempts
        /// </summary>
        private bool ValidateAbilityExecution(Ability ability, Tile targetTile = null, Vector2Int aimDir = default)
        {
            if (ability == null || ability.targeting == null) return false;

            var ctx = new AbilityContext
            {
                caster = currentActiveUnit,
                ability = ability,
                aimDir = aimDir,
                targetTile = targetTile
            };

            // Check if ability would have valid targets
            var targets = ability.targeting.SelectTargets(ctx);
            return targets.Count > 0 || ability.canExecuteWithoutTargets;
        }

        /// <summary>
        /// Executes ability with full animation coordination and state management
        /// Handles both directional and single-target abilities
        /// </summary>
        IEnumerator ExecuteAbilityWithAnimation(Ability ability, AbilityContext ctx, bool isDirectional, Vector2Int aimDir = default)
        {
            currentState = CombatState.ExecutingAction;
            isWaitingForAnimation = true;

            // NEW: Block all input during ability execution
            BlockAllInput();

            // Hide UI during ability execution for clean visuals
            UIEvents.OnAbilityAnimationStarted();

            // Clear random targeting cache if applicable
            if (ability?.targeting is RandomAOETargeting randomTargeting)
            {
                randomTargeting.OnAbilityExecuted();
            }

            // Execute the ability based on type
            bool executionSuccessful = false;
            if (isDirectional)
            {
                executionSuccessful = ability.Execute(currentActiveUnit, aimDir);
            }
            else if (ctx != null)
            {
                executionSuccessful = ability.ExecuteWithContext(ctx);
            }
            else
            {
                executionSuccessful = ability.Execute(currentActiveUnit, Vector2Int.up);
            }

            if (executionSuccessful)
            {
                // Mark ability as used
                hasUsedAbilityThisTurn = true;

                // NEW: Wait for the complete ability sequence including camera transitions
                yield return StartCoroutine(WaitForCompleteAbilitySequence(ability));

                // Restore appropriate highlights after successful execution
                RestoreDefaultHighlights();

                // Clear ability targeting state
                ClearAbilityTargeting();

                // Notify systems of ability usage
                UIEvents.OnAbilityUsed();
            }
            else
            {
                // Re-show targeting preview for retry
                if (currentAbility.targeting is SingleTargeting)
                {
                    ShowSingleTargetRangePreview();
                }
            }

            // Always complete animation cycle regardless of success/failure
            UIEvents.OnAbilityAnimationComplete();
            UIEvents.OnTargetingStateChanged();

            // NEW: Unblock all input
            UnblockAllInput();

            isWaitingForAnimation = false;
            currentState = CombatState.WaitingForInput;
        }

        private IEnumerator WaitForCompleteAbilitySequence(Ability ability)
        {
            var animator = currentActiveUnit.GetComponent<UnitAnimator>()?.GetComponent<Animator>();

            if (animator != null && !string.IsNullOrEmpty(ability.AnimationState))
            {
                // Wait for the complete sequence including camera transitions
                // This is handled by the Unit's ExecuteTimedEffects coroutine
                yield return new WaitForSeconds(0.1f);

                // Wait for the ability animation and all camera sequences to complete
                // We check if the unit is still in ability execution mode
                float maxWaitTime = 30f; // Safety timeout
                float elapsed = 0f;

                while (elapsed < maxWaitTime)
                {
                    // Check if camera is still transitioning or ability is still executing
                    bool cameraTransitioning = cameraController != null && cameraController.IsTransitioning;
                    bool abilityExecuting = currentActiveUnit.GetComponent<Unit>().currentAbilityContext != null;

                    if (!cameraTransitioning && !abilityExecuting)
                    {
                        break;
                    }

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                // Additional safety wait
                yield return new WaitForSeconds(0.2f);
            }
            else
            {
                // Fallback wait time if no animator or animation state
                yield return new WaitForSeconds(5f); // Longer to account for camera transitions
            }
        }

        /// <summary>
        /// Confirms and executes a single-target ability
        /// Validates range and target validity before execution
        /// </summary>
        void ConfirmSingleTargetAbility(Tile targetTile)
        {
            if (targetTile == null) return;

            if (currentAbility.targeting is SingleTargeting singleTargeting)
            {
                var ctx = new AbilityContext
                {
                    caster = currentActiveUnit,
                    ability = currentAbility,
                    targetTile = targetTile
                };

                // Validate range
                if (!singleTargeting.IsWithinRange(ctx, targetTile))
                {
                    Debug.Log("Target tile is out of range. Try again.");
                    return; // Stay in targeting mode
                }

                // Special validation for teleport abilities
                bool isTeleportAbility = currentAbility.effects.Exists(effect => effect is TeleportEffect);
                if (isTeleportAbility && (targetTile.occupied || !targetTile.passableTerrain))
                {
                    Debug.Log("Cannot teleport to occupied or impassable tile. Try again.");
                    return; // Stay in targeting mode
                }

                // Clear highlights immediately to prevent visual artifacts
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

                // Execute with animation coordination
                StartCoroutine(ExecuteAbilityWithAnimation(currentAbility, ctx, false));
            }
        }

        /// <summary>
        /// Confirms and executes a directional ability
        /// Pre-validates targets to avoid failed execution
        /// </summary>
        void ConfirmDirectionalAbility(Vector2Int aimDir)
        {
            // Pre-validate execution to avoid failed attempts
            if (!ValidateAbilityExecution(currentAbility, null, aimDir))
            {
                return;
            }

            // Clear highlights immediately
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Execute with animation coordination
            StartCoroutine(ExecuteAbilityWithAnimation(currentAbility, null, true, aimDir));
        }

        #endregion

        #region Ability Targeting Input Handlers

        /// <summary>
        /// Handles mouse movement during single-target ability targeting
        /// Updates hover tile and refreshes targeting preview
        /// </summary>
        private void HandleSingleTargetingMouseMove(Vector3 mouseWorldPosition)
        {
            Tile targetTile = GridManager.Instance.GetClosestTile(mouseWorldPosition, MapManager.Instance.CurrentConfiguration.tileSpacing);

            // Only update preview if hovered tile changed (performance optimization)
            if (targetTile != hoveredTile)
            {
                hoveredTile = targetTile;
                ShowSingleTargetRangePreview();
            }
        }

        /// <summary>
        /// Handles mouse movement during directional ability targeting
        /// Calculates aim direction and updates preview highlights
        /// </summary>
        private void HandleDirectionalTargetingMouseMove(Vector3 mouseWorldPosition)
        {
            Vector3 dir = (mouseWorldPosition - currentActiveUnit.transform.position);
            dir.y = 0; // Flatten to horizontal plane for 2D grid

            if (dir.sqrMagnitude > 0.1f) // Threshold to prevent jitter at origin
            {
                Vector2Int aimDir = GetCardinalDirection(dir.normalized);

                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.AbilityPreview,
                    currentActiveUnit,
                    currentAbility,
                    aimDir
                );
            }
        }

        /// <summary>
        /// Handles click confirmation for directional abilities
        /// Converts mouse position to cardinal direction
        /// </summary>
        private void HandleDirectionalAbilityClick(Vector3 mouseWorldPosition)
        {
            Vector3 dir = (mouseWorldPosition - currentActiveUnit.transform.position);
            dir.y = 0; // Flatten to horizontal plane

            if (dir.sqrMagnitude > 0.1f)
            {
                Vector2Int aimDir = GetCardinalDirection(dir.normalized);
                ConfirmDirectionalAbility(aimDir);
            }
        }

        /// <summary>
        /// Shows targeting preview for single-target abilities
        /// Highlights range and valid targets with appropriate colors
        /// </summary>
        void ShowSingleTargetRangePreview()
        {
            if (!(currentAbility.targeting is SingleTargeting singleTargeting)) return;

            var ctx = new AbilityContext
            {
                caster = currentActiveUnit,
                ability = currentAbility
            };

            // Get all tiles in range
            var tilesInRange = singleTargeting.GetTilesInRange(ctx);

            // Clear and apply base range highlights
            GridManager.Instance.ClearAllHighlights();
            foreach (var tile in tilesInRange)
            {
                tile.Highlight(TileHighlightType.Danger); // Red for danger zone
            }

            // Apply specific target highlighting for hovered tile
            if (hoveredTile != null && tilesInRange.Contains(hoveredTile))
            {
                // Check for teleport ability (different targeting rules)
                bool isTeleportAbility = currentAbility.effects.Exists(effect => effect is TeleportEffect);

                if (isTeleportAbility)
                {
                    // Teleport: highlight empty, passable tiles
                    if (hoveredTile.currentUnit == null && hoveredTile.passableTerrain)
                    {
                        hoveredTile.Highlight(TileHighlightType.AttackRange); // Green for valid teleport
                    }
                }
                else
                {
                    // Damage abilities: check unit targeting rules
                    if (hoveredTile.currentUnit != null)
                    {
                        var unit = hoveredTile.currentUnit;
                        bool isAlly = unit is EnemyUnit == currentActiveUnit is EnemyUnit;
                        bool canHit = (isAlly && currentAbility.canHitAllies) || (!isAlly && currentAbility.canHitEnemies);

                        if (canHit)
                        {
                            hoveredTile.Highlight(TileHighlightType.AttackRange); // Green for valid target
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles tile clicks during multi-tile selection targeting.
        /// Each valid click adds a tile to the selection. The ability fires automatically
        /// when the required number of unique tiles have been chosen — no separate confirm needed.
        /// </summary>
        private void HandleMultiTileSelectionClick(Tile clickedTile, MultiTileSelectionTargeting targeting)
        {
            var ctx = new AbilityContext { caster = currentActiveUnit, ability = currentAbility };

            if (!targeting.TrySelectTile(clickedTile, ctx)) return;

            if (targeting.IsComplete)
            {
                // All required tiles selected — execute immediately.
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                StartCoroutine(ExecuteAbilityWithAnimation(currentAbility, ctx, false));
            }
            else
            {
                // Refresh highlights to show the new selection state.
                ShowMultiTileSelectionPreview(targeting, ctx);
            }
        }

        /// <summary>
        /// Handles mouse movement during multi-tile selection targeting.
        /// Shows the selectable range, highlights already-chosen tiles, and previews the hovered tile.
        /// </summary>
        private void HandleMultiTileSelectionMouseMove(Vector3 mouseWorldPosition)
        {
            if (!(currentAbility.targeting is MultiTileSelectionTargeting multiTargeting)) return;

            Tile newHover = GridManager.Instance.GetClosestTile(
                mouseWorldPosition,
                MapManager.Instance.CurrentConfiguration.tileSpacing);

            if (newHover == hoveredTile) return;
            hoveredTile = newHover;

            var ctx = new AbilityContext { caster = currentActiveUnit, ability = currentAbility };
            ShowMultiTileSelectionPreview(multiTargeting, ctx);

            // Highlight the hovered tile if it's a valid selection
            if (hoveredTile != null && multiTargeting.IsValidSelection(hoveredTile, ctx))
                hoveredTile.Highlight(TileHighlightType.Occupied); // Distinct hover colour
        }

        /// <summary>
        /// Refreshes the highlight state for multi-tile selection.
        /// Selectable range = Moveable (cyan). Already selected = AttackRange (red). Hover = Occupied (dark).
        /// </summary>
        private void ShowMultiTileSelectionPreview(MultiTileSelectionTargeting targeting, AbilityContext ctx)
        {
            GridManager.Instance.ClearAllHighlights();

            // Show all tiles the player can still select
            foreach (var tile in targeting.GetTilesInRange(ctx))
            {
                if (!targeting.SelectedTiles.Contains(tile))
                    tile.Highlight(TileHighlightType.Moveable);
            }

            // Already-selected tiles get a distinct colour so the player can track their choices
            foreach (var tile in targeting.SelectedTiles)
                tile.Highlight(TileHighlightType.AttackRange);
        }

        #endregion


        // Add new methods for input blocking control
        public void BlockAllInput()
        {
            isBlockingAllInput = true;
        }

        public void UnblockAllInput()
        {
            isBlockingAllInput = false;
        }

        /// <summary>
        /// Converts a 3D direction vector to the nearest cardinal direction
        /// Essential for grid-based directional abilities
        /// </summary>
        Vector2Int GetCardinalDirection(Vector3 dir)
        {
            // Choose dominant axis and return appropriate cardinal direction
            if (Mathf.Abs(dir.x) > Mathf.Abs(dir.z))
                return dir.x > 0 ? Vector2Int.right : Vector2Int.left;
            else
                return dir.z > 0 ? Vector2Int.up : Vector2Int.down;
        }

        #pragma endregion
    }
}