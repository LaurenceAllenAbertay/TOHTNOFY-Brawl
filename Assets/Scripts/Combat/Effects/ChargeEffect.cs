using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Charge Effect")]
    public class ChargeEffect : AbilityEffect
    {
        [Header("Movement Settings")]
        public float chargeSpeed = 8f;
        public bool moveToEnd = true;
        public bool chargeUntilBlocked = false;

        [Header("Animation")]
        [SerializeField] private string moveAnimationState = "";
        
        [SerializeField] private string stopAnimationState = "";

        [Tooltip("Off (default): movement duration is Distance / Charge Speed, and the Move Animation State should loop to cover however long that ends up taking - use this for abilities whose charge distance varies. On: movement duration instead matches the Move Animation State's own clip length exactly (it should NOT loop) - the caster arrives right as it finishes. Use this when the animation is a single, fixed one-shot regardless of distance.")]
        [SerializeField] private bool matchMovementToAnimationLength = false;
        
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {

        }
        
        public IEnumerator ExecuteCharge(AbilityContext ctx, IReadOnlyList<Unit> targets, UnitAnimator casterAnimator)
        {
            if (ctx?.caster == null) yield break;

            var caster = ctx.caster;
            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            bool isPlayerUnit = caster is PlayerUnit;

            Tile destination = CalculateDestination(ctx, targets);
            if (destination == null)
            {
                RestoreHighlightingAfterCharge(caster, combatManager, isPlayerUnit);
                yield break;
            }
            
            Unit unitToDisplace = null;
            Tile casterLandingTile = destination;

            if (destination.occupied && destination.currentUnit != caster)
            {
                unitToDisplace = destination.currentUnit;
                if (!CanDisplaceUnit(unitToDisplace, ctx.aimDir))
                {
                    var traversalForLanding = (ctx.ability.targeting as MovementLineTargeting)?.GetTraversal(ctx);
                    Tile lastFreeTile = null;
                    if (traversalForLanding != null)
                    {
                        foreach (var t in traversalForLanding)
                        {
                            if (t.passableTerrain && !t.occupied)
                                lastFreeTile = t;
                        }
                    }

                    casterLandingTile = lastFreeTile ?? caster.currentTile;
                    unitToDisplace = null; 
                }
            }
            
            bool casterWillMove = casterLandingTile != caster.currentTile;

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            Vector3 startPos = caster.transform.position;
            Vector3 endPos = casterLandingTile.transform.position;
            float distance = Vector3.Distance(startPos, endPos);
            
            var traversalTiles = ctx.ability.targeting.GetTraversal(ctx);
            var passProgressByTarget = new Dictionary<Unit, float>();
            if (targets != null)
            {
                for (int i = 0; i < traversalTiles.Count; i++)
                {
                    var tileUnit = traversalTiles[i].currentUnit;
                    if (tileUnit != null && tileUnit != caster && targets.Contains(tileUnit))
                    {
                        passProgressByTarget[tileUnit] = (float)(i + 0.5f) / traversalTiles.Count;
                    }
                }
            }
            var triggeredTargets = new HashSet<Unit>();
            
            float duration;

            if (casterWillMove && matchMovementToAnimationLength &&
                !string.IsNullOrEmpty(moveAnimationState) && casterAnimator != null)
            {
                casterAnimator.ForcePlayAnimation(moveAnimationState);
                yield return null;
                duration = casterAnimator.GetCurrentAnimationLength();
            }
            else
            {
                duration = casterWillMove ? distance / chargeSpeed : 0f;

                if (!string.IsNullOrEmpty(moveAnimationState) && casterAnimator != null)
                    casterAnimator.ForcePlayAnimation(moveAnimationState);
            }

            float elapsed = 0f;
            
            if (!casterWillMove)
            {
                foreach (var kvp in passProgressByTarget)
                {
                    if (!triggeredTargets.Contains(kvp.Key))
                    {
                        triggeredTargets.Add(kvp.Key);
                        TriggerPassThroughHit(ctx, kvp.Key);
                    }
                }
            }

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easedT = Mathf.Pow(t, 0.7f);

                caster.transform.position = Vector3.Lerp(startPos, endPos, easedT);
                
                foreach (var kvp in passProgressByTarget)
                {
                    if (!triggeredTargets.Contains(kvp.Key) && t >= kvp.Value)
                    {
                        triggeredTargets.Add(kvp.Key);
                        TriggerPassThroughHit(ctx, kvp.Key);
                    }
                }

                yield return null;
            }
            
            if (casterWillMove)
                caster.transform.position = endPos;
            
            if (!string.IsNullOrEmpty(stopAnimationState) && casterAnimator != null)
            {
                casterAnimator.PlayAnimation(stopAnimationState);

                float startWait = 0f;
                while (startWait < 1f && !casterAnimator.IsPlayingAnimation(stopAnimationState))
                {
                    yield return null;
                    startWait += Time.deltaTime;
                }

                while (casterAnimator.IsPlayingAnimation(stopAnimationState) &&
                       casterAnimator.GetCurrentAnimationTime() < 1f)
                {
                    yield return null;
                }
            }
            else if (casterAnimator != null)
            {
                casterAnimator.PlayIdle();
            }

            if (unitToDisplace != null)
                yield return caster.StartCoroutine(ApplyChargeDisplacementWithAnimation(unitToDisplace, ctx.aimDir));
            
            caster.SetCurrentTileLogical(casterLandingTile);

            RestoreHighlightingAfterCharge(caster, combatManager, isPlayerUnit);
        }
        
        private void TriggerPassThroughHit(AbilityContext ctx, Unit target)
        {
            target.GetComponent<UnitAnimator>()?.PlayHurt();

            CombatVFXManager.Instance?.SpawnHitEffects(ctx, new List<Unit> { target });
            
            var singleTarget = new List<Unit> { target };
            foreach (var effect in ctx.ability.effects)
            {
                if (effect == null) continue;
                if (effect.AnimationPhase == EffectAnimationPhase.Displacement) continue;
                if (effect.AnimationPhase == EffectAnimationPhase.PreEffect) continue;
                if (effect.IsSelfOnly(ctx)) continue;
                effect.Apply(ctx, singleTarget);
            }
        }
        
        private IEnumerator ApplyChargeDisplacementWithAnimation(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace?.currentTile == null) yield break;
            
            Tile displacementTile = FindDisplacementTile(unitToDisplace, chargeDirection);
            if (displacementTile == null)
            {
                Debug.LogWarning($"ChargeEffect: No valid displacement tile found for {unitToDisplace.name}");
                yield break;
            }

            if (unitToDisplace.IsBody)
            {
                bool bodyMoveComplete = false;
                unitToDisplace.AnimateToTile(displacementTile, 0.3f, () => bodyMoveComplete = true);
                while (!bodyMoveComplete) yield return null;
                yield break;
            }

            var unitAnimator = unitToDisplace.GetComponent<UnitAnimator>();
            if (unitAnimator == null)
            {
                DisplaceUnitInstant(unitToDisplace, chargeDirection);
                yield break;
            }

            bool isPlayerUnit = unitToDisplace is PlayerUnit;
            var combatManager = Object.FindAnyObjectByType<CombatManager>();

            if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == unitToDisplace)
                combatManager.BlockAnimationForEffect();
            
            unitAnimator.PlayKnockbackStart();
            
            yield return new WaitForSeconds(0.1f);

            float movementDuration = 0.3f;
            bool movementComplete = false;
            unitToDisplace.AnimateToTile(displacementTile, movementDuration, () => movementComplete = true);

            while (!movementComplete)
            {
                yield return null;
            }

            unitAnimator.PlayKnockbackEnd();
            yield return new WaitForSeconds(0.3f);
            
            if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == unitToDisplace)
            {
                yield return combatManager.StartCoroutine(RestorePlayerStateAfterDisplacement(combatManager, unitToDisplace));
            }
        }
        
        private Tile FindDisplacementTile(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace?.currentTile == null) return null;

            var currentTile = unitToDisplace.currentTile;
            Vector2Int[] perpendiculars = GridDirectionUtility.Perpendiculars(chargeDirection);
            Vector2Int backward = GridDirectionUtility.Opposite(chargeDirection);

            foreach (var dir in perpendiculars)
            {
                var tile = GridManager.Instance.GetTileInDirection(currentTile, dir);
                if (tile != null && tile.passableTerrain && !tile.occupied &&
                    GridManager.Instance.IsSameYLevel(currentTile, tile))
                    return tile;
            }

            var backTile = GridManager.Instance.GetTileInDirection(currentTile, backward);
            if (backTile != null && backTile.passableTerrain && !backTile.occupied &&
                GridManager.Instance.IsSameYLevel(currentTile, backTile))
                return backTile;

            var adjacentTiles = GridManager.Instance.GetAdjacentTiles(currentTile);
            foreach (var tile in adjacentTiles)
            {
                if (tile.passableTerrain && !tile.occupied)
                    return tile;
            }

            return null; 
        }
        
        private IEnumerator RestorePlayerStateAfterDisplacement(CombatManager combatManager, Unit displacedUnit)
        {
            yield return null;
            
            combatManager.ReleaseAnimationBlock();
            
            if (combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    displacedUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }
            
            UIEvents.OnCombatStateChanged();
            UIEvents.OnUnitMoved();
        }
        
        private void RestoreHighlightingAfterCharge(Unit caster, CombatManager combatManager, bool isPlayerUnit)
        {
            if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == caster)
            {
                if (combatManager.CanMove)
                {
                    GridManager.Instance.SetHighlightMode(
                        GridManager.HighlightMode.Movement,
                        caster,
                        movementRangeOverride: combatManager.GetRemainingMovement());
                }
            }
        }

        private Tile CalculateDestination(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx.ability.targeting is not MovementLineTargeting lineTargeting)
                return ctx.caster.currentTile;

            var tiles = lineTargeting.GetTraversal(ctx);
            if (tiles.Count == 0) return ctx.caster.currentTile;

            Tile finalTile = null;

            if (chargeUntilBlocked)
            {
                for (int i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    if (tile.occupied && tile.currentUnit != ctx.caster)
                    {
                        if (i > 0 && tiles[i - 1].passableTerrain)
                        {
                            finalTile = tiles[i - 1];
                        }
                        break;
                    }
                    else if (tile.passableTerrain)
                    {
                        finalTile = tile;
                    }
                }
            }
            else
            {
                if (moveToEnd)
                {
                    for (int i = tiles.Count - 1; i >= 0; i--)
                    {
                        if (tiles[i].passableTerrain && !tiles[i].occupied)
                        {
                            finalTile = tiles[i];
                            break;
                        }
                    }

                    if (finalTile == null)
                    {
                        for (int i = tiles.Count - 1; i >= 0; i--)
                        {
                            if (tiles[i].passableTerrain)
                            {
                                finalTile = tiles[i];
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (targets != null && targets.Count > 0)
                    {
                        var lastTarget = targets[targets.Count - 1];
                        int lastTargetIndex = tiles.IndexOf(lastTarget.currentTile);

                        if (lastTargetIndex >= 0 && lastTargetIndex < tiles.Count - 1)
                        {
                            for (int i = lastTargetIndex + 1; i < tiles.Count; i++)
                            {
                                var tile = tiles[i];
                                if (tile.passableTerrain)
                                {
                                    finalTile = tile;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return finalTile ?? ctx.caster.currentTile;
        }

        private bool CanDisplaceUnit(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace == null) return false;

            var currentTile = unitToDisplace.currentTile;
            Vector2Int[] perpendiculars = GridDirectionUtility.Perpendiculars(chargeDirection);
            Vector2Int backward = GridDirectionUtility.Opposite(chargeDirection);

            foreach (var dir in perpendiculars)
            {
                var tile = GridManager.Instance.GetTileInDirection(currentTile, dir);
                if (tile != null && tile.passableTerrain && !tile.occupied &&
                    GridManager.Instance.IsSameYLevel(currentTile, tile))
                    return true;
            }

            var backTile = GridManager.Instance.GetTileInDirection(currentTile, backward);
            if (backTile != null && backTile.passableTerrain && !backTile.occupied &&
                GridManager.Instance.IsSameYLevel(currentTile, backTile))
                return true;

            var adjacentTiles = GridManager.Instance.GetAdjacentTiles(currentTile);
            foreach (var tile in adjacentTiles)
            {
                if (!tile.occupied)
                    return true;
            }

            return false; 
        }

        private bool DisplaceUnitInstant(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace == null) return false;

            Tile displacementTile = FindDisplacementTile(unitToDisplace, chargeDirection);
            if (displacementTile == null)
            {
                Debug.LogWarning($"ChargeEffect: No displacement possible for {unitToDisplace.name}, destroying unit");
                unitToDisplace.ReceiveDamage(999);
                return true;
            }

            unitToDisplace.SetCurrentTile(displacementTile);
            Debug.Log($"ChargeEffect: Instantly displaced {unitToDisplace.name} to {displacementTile.gridPosition}");
            return true;
        }

    }
}