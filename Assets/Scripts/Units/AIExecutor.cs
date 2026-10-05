using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{

    public class AIExecutor : MonoBehaviour
    {
        private Unit unit;
        private JumpSystem jumpSystem;
        private AIDebugLogger logger;

        public void Initialize(Unit unit, JumpSystem jumpSystem, AIDebugLogger logger)
        {
            this.unit = unit;
            this.jumpSystem = jumpSystem;
            this.logger = logger;
        }
        
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

        private IEnumerator ExecuteMovement(ActionPlan plan)
        {
            if (!plan.isJump)
            {
                logger?.LogMovement("moving", plan.movementTarget);
                yield return StartCoroutine(ExecuteRegularMovement(plan.movementTarget));
                yield break;
            }

            if (jumpSystem == null)
            {
                Debug.LogWarning($"[{unit.name}] JumpSystem not found - falling back to regular movement");
                yield return StartCoroutine(ExecuteRegularMovement(plan.movementTarget));
                yield break;
            }

            if (plan.jumpFromTile != null && plan.jumpFromTile != unit.currentTile)
            {
                logger?.LogMovement("moving", plan.jumpFromTile);
                yield return StartCoroutine(ExecuteRegularMovement(plan.jumpFromTile));

                if (unit.currentTile != plan.jumpFromTile)
                {
                    Debug.LogWarning($"[{unit.name}] Could not reach jump launch tile {plan.jumpFromTile.name} - skipping jump");
                    yield break;
                }
            }

            logger?.LogMovement("jumping", plan.movementTarget);
            yield return StartCoroutine(ExecuteAIJump(plan.movementTarget));
        }

        private IEnumerator ExecuteRegularMovement(Tile destination)
        {
            if (UnitMovementController.Instance == null)
            {
                Debug.LogError($"[{unit.name}] UnitMovementController not found");
                yield break;
            }

            var waypoints = GridManager.Instance.FindPathOptimized(
                unit.currentTile, destination, unit.GetEffectiveMovementRange());

            if (waypoints.Count > 0)
            {
                var camera = CameraController.Instance;
                int focusHandle = camera != null ? camera.PushFollow(unit, 1f) : -1;

                yield return StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                    unit,
                    destination,
                    waypoints));

                if (focusHandle >= 0)
                    camera.PopFocus(focusHandle);
            }
        }

        private IEnumerator ExecuteAIJump(Tile destination)
        {
            if (!CanAIJumpToTile(destination))
            {
                Debug.LogError($"[{unit.name}] Invalid jump destination: {destination?.name ?? "null"}");
                yield break;
            }

            Unit stompTarget = destination.occupied ? destination.currentUnit : null;
            Tile originTile  = unit.currentTile;

            Vector3 startPos = unit.transform.position;
            Vector3 endPos = destination.transform.position;

            unit.SetCurrentTileLogical(destination);

            var cam = CameraController.Instance;
            int focusHandle = -1;
            if (cam != null)
                focusHandle = cam.PushFocus(cam.WorldFocusPosition(endPos));

            yield return StartCoroutine(jumpSystem.JumpAnimation(unit, startPos, endPos, stompTarget, originTile));

            if (focusHandle >= 0)
                cam.PopFocus(focusHandle);
        }

        private bool CanAIJumpToTile(Tile targetTile)
            => jumpSystem != null && jumpSystem.IsValidJumpDestination(unit, unit.currentTile, targetTile);

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

                    bool anyKnockbackPlaying = false;
                    foreach (var otherUnit in UnitManager.AllUnits)
                    {
                        var ua = otherUnit.GetComponent<UnitAnimator>();
                        if (ua != null && ua.IsInKnockbackSequence)
                        {
                            anyKnockbackPlaying = true;
                            break;
                        }
                    }

                    bool bigMomentActive = BigMomentSequencer.Instance != null && BigMomentSequencer.Instance.HasPending;

                    if (!cameraTransitioning && !abilityExecuting && !anyKnockbackPlaying && !bigMomentActive) break;
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

    }
}