using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Position Swap Effect")]
    public class PositionSwapEffect : AbilityEffect
    {
        [Header("Swap Pathing")]
        [Tooltip("Extra steps the swap route may spend beyond the direct grid distance. " +
                 "Because any walked detour on a grid costs two steps, values below 2 allow no detour at all.")]
        public int pathLengthSlack = 2;

        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;

        public override bool IsRunning => _runningRoutes > 0;

        private int _runningRoutes;

        public override bool CanConfirmTargetTile(AbilityContext ctx, Tile tile)
        {
            return TryBuildSwapPath(ctx?.caster, ctx?.ability, tile, out _);
        }

        public bool TryBuildSwapPath(Unit caster, Ability ability, Tile targetTile, out List<PathStep> path)
        {
            path = null;

            if (caster == null || ability == null || targetTile == null) return false;
            if (caster.currentTile == null || GridManager.Instance == null) return false;

            Unit target = targetTile.currentUnit;
            if (target == null || target == caster)
            {
                Debug.Log("[PositionSwap] No unit on the target tile to swap with. Try again.");
                return false;
            }

            if (target.IsDead || target.IsBody)
            {
                Debug.Log($"[PositionSwap] {target.name} is downed and cannot be swapped with. Try again.");
                return false;
            }

            if (!ability.CanHit(caster, target))
            {
                Debug.Log($"[PositionSwap] {target.name} is not a legal target for {ability.abilityName}. Try again.");
                return false;
            }

            int maxCost = GridManager.Instance.GetGridDistance(caster.currentTile, targetTile, includeYLevel: true)
                          + Mathf.Max(0, pathLengthSlack);

            if (!JumpAwarePathfinder.TryFindPath(caster.currentTile, targetTile, maxCost, out path))
            {
                Debug.Log($"[PositionSwap] No route to {target.name} within {maxCost} steps. Try again.");
                return false;
            }

            return true;
        }

        public override bool NeedsCameraPreview(AbilityContext ctx, out Tile focusTile)
        {
            focusTile = ctx?.targetTile;
            return focusTile != null;
        }

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            _runningRoutes = 0;

            if (ctx?.caster == null || ctx.caster.currentTile == null) return;

            if (UnitMovementController.Instance == null)
            {
                Debug.LogError("[PositionSwap] No UnitMovementController in the scene — swap aborted.");
                return;
            }

            Unit target = ctx.targetTile != null ? ctx.targetTile.currentUnit : null;
            if (target == null || targets == null || !targets.Contains(target))
            {
                Debug.Log("[PositionSwap] The target tile no longer holds a legal target — swap aborted.");
                return;
            }

            if (!TryBuildSwapPath(ctx.caster, ctx.ability, ctx.targetTile, out List<PathStep> casterRoute))
                return;

            var jumpSystem = Object.FindAnyObjectByType<JumpSystem>();
            if (jumpSystem == null && casterRoute.Any(step => step.stepType == PathStepType.Jump))
            {
                Debug.LogWarning("[PositionSwap] The route needs a jump but there is no JumpSystem in the scene — swap aborted.");
                return;
            }

            Tile casterOrigin = ctx.caster.currentTile;
            Tile targetOrigin = target.currentTile;

            List<PathStep> targetRoute = BuildReturnRoute(casterOrigin, casterRoute);

            casterOrigin.currentUnit = null;
            targetOrigin.currentUnit = null;

            _runningRoutes = 2;

            ConsumeCasterMovement(ctx.caster);

            var host = UnitMovementController.Instance;
            host.StartCoroutine(RunRoute(ctx.caster, casterRoute, targetOrigin, jumpSystem));
            host.StartCoroutine(RunRoute(target, targetRoute, casterOrigin, jumpSystem));
        }

        private static List<PathStep> BuildReturnRoute(Tile casterOrigin, List<PathStep> casterRoute)
        {
            var route = new List<PathStep>(casterRoute.Count);

            for (int i = casterRoute.Count - 1; i >= 0; i--)
            {
                Tile destination = i > 0 ? casterRoute[i - 1].tile : casterOrigin;
                route.Add(new PathStep(destination, casterRoute[i].stepType));
            }

            return route;
        }

        private IEnumerator RunRoute(Unit unit, List<PathStep> route, Tile destination, JumpSystem jumpSystem)
        {
            var movement = UnitMovementController.Instance;
            var animator = unit.GetComponent<UnitAnimator>();
            var sprites  = unit.GetComponentsInChildren<SpriteRenderer>(includeInactive: false);

            Tile originTile      = unit.currentTile;
            Tile lastEnteredTile = originTile;
            int index = 0;

            while (index < route.Count)
            {
                if (route[index].stepType == PathStepType.Walk)
                {
                    var run = new List<Tile>();

                    while (index < route.Count && route[index].stepType == PathStepType.Walk)
                    {
                        run.Add(route[index].tile);
                        index++;

                        if (run[run.Count - 1].HasActiveEffects) break;
                    }

                    if (animator != null)
                        animator.PlayMove();

                    float duration = WalkDuration(unit.transform.position, run, movement.MovementSpeed);

                    yield return movement.StartCoroutine(movement.MoveAlongWaypoints(
                        unit, run, duration, sprites, playDustFX: true, claimTilesLogically: false));

                    lastEnteredTile = run[run.Count - 1];
                }
                else
                {
                    Tile jumpTile = route[index].tile;
                    index++;

                    yield return movement.StartCoroutine(jumpSystem.JumpAnimation(
                        unit,
                        unit.transform.position,
                        jumpTile.transform.position,
                        stompTarget: null,
                        originTile: null,
                        stompDamageOverride: -1,
                        fireMovementUIEvents: false));

                    lastEnteredTile = jumpTile;

                    if (jumpTile.HasActiveEffects)
                        yield return movement.StartCoroutine(jumpTile.TriggerOnEnterEffects(unit));

                    if (BigMomentSequencer.Instance != null)
                        yield return movement.StartCoroutine(BigMomentSequencer.Instance.DrainQueue());
                }

                if (IsDown(unit))
                {
                    StopWhereItFell(unit, lastEnteredTile);
                    FinishRoute();
                    yield break;
                }
            }

            if (animator != null)
                animator.PlayIdle();

            FaceArrivalDirection(unit, route, originTile, destination);

            unit.SetCurrentTile(destination);
            UIEvents.OnUnitMoved();

            FinishRoute();
        }

        private static void ConsumeCasterMovement(Unit caster)
        {
            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            if (combatManager == null) return;
            if (combatManager.CurrentActiveUnit != caster) return;

            combatManager.SetMovementUsed();
        }

        private void FinishRoute()
        {
            _runningRoutes = Mathf.Max(0, _runningRoutes - 1);
        }

        private static void FaceArrivalDirection(Unit unit, List<PathStep> route, Tile originTile, Tile destination)
        {
            if (unit == null || destination == null || route.Count == 0) return;

            Tile previous = route.Count >= 2 ? route[route.Count - 2].tile : originTile;
            if (previous == null) return;

            float dx = destination.transform.position.x - previous.transform.position.x;
            if (Mathf.Abs(dx) <= 0.01f) return;

            unit.FaceDirection(dx > 0 ? Vector2Int.right : Vector2Int.left);
        }

        private static bool IsDown(Unit unit)
        {
            return unit == null || unit.IsDead || unit.currentHealth <= 0;
        }

        private static void StopWhereItFell(Unit unit, Tile tile)
        {
            if (unit == null || tile == null) return;

            unit.currentTile = tile;
            unit.transform.position = tile.transform.position;
        }

        private static float WalkDuration(Vector3 from, List<Tile> run, float speed)
        {
            float distance = 0f;
            Vector3 previous = from;

            foreach (var tile in run)
            {
                distance += Vector3.Distance(previous, tile.transform.position);
                previous = tile.transform.position;
            }

            return Mathf.Max(distance, 0.001f) / Mathf.Max(speed, 0.001f);
        }
    }
}