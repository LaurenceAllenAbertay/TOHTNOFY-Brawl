using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Movement Effect")]
    public class MovementEffect : AbilityEffect
    {
        public Vector2Int moveDirection = Vector2Int.down;
        
        public int moveDistance = 1;

        public bool applyToCaster = false;

        [Header("Animation Settings")]
        public float movementDurationPerTile = 0.3f;

        public override EffectAnimationPhase AnimationPhase =>
            applyToCaster ? EffectAnimationPhase.PreEffect : EffectAnimationPhase.Displacement;
        public override string TargetAnimationHint => applyToCaster ? null : "Movement";
        public override float ExpectedAnimationDuration => movementDurationPerTile * moveDistance + 0.3f;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;

            if (applyToCaster)
            {
                if (ctx.caster.currentTile != null)
                    ctx.caster.StartCoroutine(ApplyMovementWithAnimation(ctx, ctx.caster));
            }
            else
            {
                if (targets != null)
                {
                    foreach (var target in targets)
                    {
                        if (target?.currentTile != null)
                            ctx.caster.StartCoroutine(ApplyMovementWithAnimation(ctx, target));
                    }
                }
            }
        }

        private IEnumerator ApplyMovementWithAnimation(AbilityContext ctx, Unit unitToMove)
        {
            Vector2Int actualDirection = CalculateMovementDirection(moveDirection, ctx.aimDir);

            var movementPath = CalculateMovementPath(unitToMove, actualDirection);

            if (movementPath.Count == 0)
            {
                Debug.Log($"{unitToMove.name} couldn't move {GetDirectionName(actualDirection)} - blocked or no valid tile");
                yield break;
            }

            Debug.Log($"{unitToMove.name} starting movement: {movementPath.Count} tiles in direction {GetDirectionName(actualDirection)}");
            
            bool isActivePlayer = IsActivePlayerUnit(unitToMove);
            if (isActivePlayer)
            {
                BlockPlayerInputDuringMovement();
            }
            
            var unitAnimator = unitToMove.GetComponent<UnitAnimator>();
            var spriteRenderers = unitToMove.GetComponentsInChildren<SpriteRenderer>(includeInactive: false);
            
            if (unitAnimator != null)
            {
                unitAnimator.PlayMove();
            }
            
            Vector3 currentPos = unitToMove.transform.position;

            foreach (var tile in movementPath)
            {
                Vector3 segmentStart = currentPos;
                Vector3 segmentEnd = tile.transform.position;
                float segmentDuration = movementDurationPerTile;
                
                if (spriteRenderers != null)
                {
                    Vector3 direction = (segmentEnd - segmentStart).normalized;

                    if (Mathf.Abs(direction.x) > 0.1f)
                    {
                        bool flipX = direction.x > 0;
                        foreach (var sr in spriteRenderers)
                        {
                            if (sr == null) continue;
                            sr.flipX = flipX;
                        }
                    }
                }
                
                float segmentElapsed = 0f;
                while (segmentElapsed < segmentDuration)
                {
                    segmentElapsed += Time.deltaTime;
                    float segmentT = segmentElapsed / segmentDuration;
                    
                    float smoothSegmentT = Mathf.SmoothStep(0f, 1f, segmentT);
                    unitToMove.transform.position = Vector3.Lerp(segmentStart, segmentEnd, smoothSegmentT);

                    yield return null;
                }

                currentPos = segmentEnd;
                
                unitToMove.SetCurrentTileLogical(tile);
                
                if (tile.HasActiveEffects)
                    yield return tile.TriggerOnEnterEffects(unitToMove);
            }
            
            if (movementPath.Count > 0)
            {
                unitToMove.transform.position = movementPath[movementPath.Count - 1].transform.position;
                unitToMove.SetCurrentTile(movementPath[movementPath.Count - 1]);
            }
            
            if (unitAnimator != null)
            {
                unitAnimator.PlayIdle();
            }
            
            if (isActivePlayer)
            {
                RestorePlayerInputAfterMovement();
            }
        }

        private List<Tile> CalculateMovementPath(Unit unit, Vector2Int direction)
        {
            var path = new List<Tile>();
            Tile current = unit.currentTile;

            for (int i = 0; i < moveDistance; i++)
            {
                var next = GridManager.Instance.GetTileInDirection(current, direction);
                if (next == null || !next.moveable || next.occupied)
                    break;

                path.Add(next);
                current = next;
            }

            return path;
        }

        private Vector2Int CalculateMovementDirection(Vector2Int relativeDirection, Vector2Int aimDirection)
        {
            if (relativeDirection == Vector2Int.down)
            {
                return GetOppositeDirection(aimDirection);
            }
            
            return RotateDirection(relativeDirection, aimDirection);
        }

        private Vector2Int GetOppositeDirection(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return Vector2Int.down;
            if (direction == Vector2Int.down) return Vector2Int.up;
            if (direction == Vector2Int.left) return Vector2Int.right;
            if (direction == Vector2Int.right) return Vector2Int.left;

            return Vector2Int.zero;
        }

        private Vector2Int RotateDirection(Vector2Int dir, Vector2Int aimDir)
        {
            int rotation = 0;
            if (aimDir == Vector2Int.right) rotation = 1;
            else if (aimDir == Vector2Int.down) rotation = 2;
            else if (aimDir == Vector2Int.left) rotation = 3;
            
            Vector2Int result = dir;
            for (int i = 0; i < rotation; i++)
            {
                result = new Vector2Int(-result.y, result.x);
            }

            return result;
        }

        private bool IsActivePlayerUnit(Unit unit)
        {
            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            return combatManager != null &&
                   combatManager.CurrentActiveUnit == unit &&
                   unit is PlayerUnit;
        }

        private void BlockPlayerInputDuringMovement()
        {
            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            if (combatManager != null)
            {
                if (GridManager.Instance != null)
                {
                    GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
                }

                combatManager.BlockAnimationForEffect();
                Debug.Log("Blocked player input during movement effect");
            }
        }

        private void RestorePlayerInputAfterMovement()
        {
            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            if (combatManager != null)
            {
                combatManager.StartCoroutine(DelayedInputRestore(combatManager));
            }
        }

        private IEnumerator DelayedInputRestore(CombatManager combatManager)
        {
            yield return null;
            
            combatManager.ReleaseAnimationBlock();
            
            if (combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    combatManager.CurrentActiveUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }
            
            UIEvents.OnCombatStateChanged();
            UIEvents.OnUnitMoved();

            Debug.Log("Restored player input after movement effect");
        }

        private string GetDirectionName(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return "north";
            if (direction == Vector2Int.down) return "south";
            if (direction == Vector2Int.left) return "west";
            if (direction == Vector2Int.right) return "east";
            return "unknown";
        }
    }
}