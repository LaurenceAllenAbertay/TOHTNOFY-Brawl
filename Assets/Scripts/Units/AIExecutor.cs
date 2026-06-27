using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Owns all AI action execution coroutines (movement, jumping, ability use).
    /// Must be a MonoBehaviour because it runs coroutines.
    /// Attach to the same prefab as UnitAI.
    /// </summary>
    public class AIExecutor : MonoBehaviour
    {
        #region Private Fields

        private Unit unit;
        private JumpSystem jumpSystem;
        private AIDebugLogger logger;

        #endregion

        #region Initialization

        public void Initialize(Unit unit, JumpSystem jumpSystem, AIDebugLogger logger)
        {
            this.unit = unit;
            this.jumpSystem = jumpSystem;
            this.logger = logger;
        }

        #endregion

        #region Action Plan Execution

        /// <summary>
        /// Executes the full action plan in the plan's order.
        /// </summary>
        public IEnumerator ExecuteActionPlan(ActionPlan plan, float actionDelay)
        {
            if (plan == null) yield break;
            if (unit == null)
            {
                Debug.LogError("[AIExecutor] ExecuteActionPlan called before Initialize.");
                yield break;
            }

            logger?.LogActionExecution();

            bool hasMovement = plan.movementTarget != null && plan.movementTarget != unit.currentTile;
            bool hasAbility = plan.abilityToUse != null;

            if (plan.isAbilityFirst)
            {
                if (hasAbility)
                {
                    yield return StartCoroutine(ExecuteAbility(plan));
                    yield return new WaitForSeconds(actionDelay);
                }

                if (hasMovement)
                {
                    yield return StartCoroutine(ExecuteMovement(plan));
                    yield return new WaitForSeconds(actionDelay);
                }
            }
            else
            {
                if (hasMovement)
                {
                    yield return StartCoroutine(ExecuteMovement(plan));
                    yield return new WaitForSeconds(actionDelay);
                }

                if (hasAbility)
                {
                    yield return StartCoroutine(ExecuteAbility(plan));
                    yield return new WaitForSeconds(actionDelay);
                }
            }

            logger?.LogActionComplete();
        }

        #endregion

        #region Movement

        private IEnumerator ExecuteMovement(ActionPlan plan)
        {
            logger?.LogMovement(plan.isJump ? "jumping" : "moving", plan.movementTarget);

            if (plan.isJump)
            {
                if (jumpSystem != null)
                {
                    yield return StartCoroutine(ExecuteAIJump(plan.movementTarget));
                }
                else
                {
                    Debug.LogWarning($"[{unit.name}] JumpSystem not found - falling back to regular movement");
                    yield return StartCoroutine(ExecuteRegularMovement(plan));
                }
            }
            else
            {
                yield return StartCoroutine(ExecuteRegularMovement(plan));
            }
        }

        private IEnumerator ExecuteRegularMovement(ActionPlan plan)
        {
            if (UnitMovementController.Instance == null)
            {
                Debug.LogError($"[{unit.name}] UnitMovementController not found");
                yield break;
            }

            var waypoints = GridManager.Instance.FindPathOptimized(
                unit.currentTile, plan.movementTarget, unit.GetEffectiveMovementRange());

            if (waypoints.Count > 0)
            {
                yield return StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                    unit,
                    plan.movementTarget,
                    waypoints,
                    followCameraForAI: true));
            }
        }

        private IEnumerator ExecuteAIJump(Tile destination)
        {
            if (!CanAIJumpToTile(destination))
            {
                Debug.LogError($"[{unit.name}] Invalid jump destination: {destination.name}");
                yield break;
            }

            unit.SetCurrentTileLogical(destination);

            Vector3 startPos = unit.transform.position;
            Vector3 endPos = destination.transform.position;

            // For AI jumps the camera lerps to the destination tile while the unit is in the air.
            // We start the transition without yielding so it runs in parallel with JumpAnimation.
            // Player jumps intentionally have no camera movement — the player controls the camera.
            var cam = CameraController.Instance;
            if (cam != null)
                StartCoroutine(cam.TransitionTo(cam.WorldFocusPosition(endPos)));

            yield return StartCoroutine(jumpSystem.JumpAnimation(unit, startPos, endPos));
        }

        private bool CanAIJumpToTile(Tile targetTile)
        {
            if (targetTile == null) return false;

            int distance = GridManager.Instance.GetGridDistance(unit.currentTile, targetTile, true);
            int maxRange = unit.JumpRange;
            const int minRange = 2;

            if (distance < minRange || distance > maxRange) return false;
            if (!targetTile.passableTerrain) return false;

            // Occupied tiles are only valid when the unit can stomp.
            if (targetTile.occupied && !unit.CanStompOccupiedTiles) return false;

            return !IsJumpBlockedByWalls(unit.currentTile, targetTile);
        }

        private bool IsJumpBlockedByWalls(Tile startTile, Tile targetTile)
        {
            if (startTile == null || targetTile == null) return true;
            Vector3 rayStart = startTile.transform.position + Vector3.up * 0.5f;
            Vector3 rayEnd = targetTile.transform.position + Vector3.up * 0.5f;
            float checkDistance = Vector3.Distance(rayStart, rayEnd) * 0.9f;
            int wallsLayerMask = LayerMask.GetMask("Walls");
            return Physics.Raycast(rayStart, (rayEnd - rayStart).normalized, checkDistance, wallsLayerMask);
        }

        #endregion

        #region Ability Execution

        private IEnumerator ExecuteAbility(ActionPlan plan)
        {
            var ability = plan.abilityToUse;
            logger?.LogAbilityExecution(plan);

            var ctx = new AbilityContext
            {
                caster = unit,
                ability = ability,
                aimDir = plan.aimDirection,
                targetTile = plan.targetTile
            };

            if (ability.targeting is MultiTileSelectionTargeting multiSel && plan.preSelectedTiles != null)
            {
                ctx = new AbilityContext { caster = unit, ability = ability };
                multiSel.BeginSelection(ctx);
                foreach (var tile in plan.preSelectedTiles)
                {
                    multiSel.TrySelectTile(tile, ctx);
                }
            }

            bool shouldUseContextExecution =
                plan.targetTile != null ||
                (ability.targeting is MultiTileSelectionTargeting && plan.preSelectedTiles != null);

            bool success = shouldUseContextExecution
                ? ability.ExecuteWithContext(ctx)
                : ability.Execute(unit, plan.aimDirection);

            if (success)
            {
                var cameraController = FindAnyObjectByType<CameraController>();
                float maxWaitTime = 30f;
                float elapsed = 0f;

                while (elapsed < maxWaitTime)
                {
                    bool cameraTransitioning = cameraController != null && cameraController.IsTransitioning;
                    bool abilityExecuting = unit.currentAbilityContext != null;
                    if (!cameraTransitioning && !abilityExecuting) break;
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                logger?.LogAbilitySuccess(ability);
            }
            else
            {
                logger?.LogAbilityFailure(ability);
            }
        }

        #endregion
    }
}