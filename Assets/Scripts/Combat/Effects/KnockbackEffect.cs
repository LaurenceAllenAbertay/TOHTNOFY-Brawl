using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Knockback Effect")]
    public class KnockbackEffect : AbilityEffect
    {
        public int knockbackDistance = 1;
        
        public bool finalTargetOnly = false;

        public bool enableCollisions = false;

        public int collisionDamage = -1;

        public bool applyToSelf = false;

        public bool radialKnockback = false;

        [Header("Animation Timing")]
        public float knockbackStartDuration = 0.5f;

        public float movementDurationPerTile = 0.2f;

        public float knockbackEndDuration = 0.5f;

        public override EffectAnimationPhase AnimationPhase =>
            applyToSelf ? EffectAnimationPhase.PreEffect : EffectAnimationPhase.Displacement;
        public override string TargetAnimationHint => applyToSelf ? null : "Knockback";
        public override float ExpectedAnimationDuration => 1f;
        
        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;

            bool singleTarget    = (targets == null || targets.Count == 1) && !applyToSelf;
            bool shouldFollowCam = singleTarget && knockbackDistance > 1;

            if (applyToSelf)
            {
                ctx.caster.StartCoroutine(ApplyKnockbackWithAnimation(ctx, ctx.caster, false));
            }
            else if (finalTargetOnly)
            {
                Unit displaced = GetDisplacedUnit(ctx);
                if (displaced != null)
                    ctx.caster.StartCoroutine(ApplyKnockbackWithAnimation(ctx, displaced, shouldFollowCam));
            }
            else
            {
                if (targets == null) return;
                foreach (var target in targets)
                {
                    if (target != null)
                        ctx.caster.StartCoroutine(ApplyKnockbackWithAnimation(ctx, target, shouldFollowCam));
                }
            }
        }

        private IEnumerator ApplyKnockbackWithAnimation(AbilityContext ctx, Unit target, bool followCamera = false)
        {
            if (target?.currentTile == null) yield break;
            
            Tile originalTile = target.currentTile;
            
            Vector2Int knockbackDir = ResolveKnockbackDir(ctx, target);
            
            if (target.IsBody)
            {
                var bodyPath = CalculateKnockbackPath(target, knockbackDir);
                if (bodyPath.Count > 0)
                {
                    bool done = false;
                    target.AnimateToTile(bodyPath[bodyPath.Count - 1],
                        movementDurationPerTile * bodyPath.Count, () => done = true);
                    while (!done) yield return null;
                }
                yield break;
            }

            bool isPlayerSelfKnockback = applyToSelf && target == ctx.caster && target is PlayerUnit;
            if (isPlayerSelfKnockback)
                ResetPlayerStateAfterKnockback(target);

            var unitAnimator = target.GetComponent<UnitAnimator>();
            if (unitAnimator == null)
            {
                ApplyKnockbackImmediate(target, knockbackDir);
                yield break;
            }
            
            var knockbackPath = CalculateKnockbackPath(target, knockbackDir);
            
            target.FaceDirection(GridDirectionUtility.Opposite(knockbackDir));
            
            if (knockbackPath.Count == 0)
            {
                unitAnimator.PlayKnockbackStart();
                yield return new WaitForSeconds(knockbackStartDuration);
                unitAnimator.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);

                if (isPlayerSelfKnockback)
                    RestorePlayerStateAfterKnockback(target);

                yield break;
            }
            
            Vector2Int resolvedDir = GridDirectionUtility.FromTiles(originalTile, knockbackPath[0]);

            unitAnimator.PlayKnockbackStart();

            if (knockbackPath.Count == 1)
            {
                yield return new WaitForSeconds(0.1f);

                bool movementComplete = false;
                target.AnimateToTile(knockbackPath[0], movementDurationPerTile, () => movementComplete = true);
                while (!movementComplete) yield return null;

                if (enableCollisions)
                {
                    Tile beyondTile = GridManager.Instance.GetTileInDirection(knockbackPath[0], resolvedDir);
                    if (beyondTile?.currentUnit != null && beyondTile.currentUnit != target)
                        yield return HandleCollision(ctx, target, beyondTile.currentUnit, resolvedDir);
                }

                float remainingStart = knockbackStartDuration - 0.1f - movementDurationPerTile;
                if (remainingStart > 0f)
                    yield return new WaitForSeconds(remainingStart);

                unitAnimator.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);
            }
            else
            {
                var camera = followCamera ? Object.FindAnyObjectByType<CameraController>() : null;
                int followHandle = -1;
                if (camera != null)
                {
                    float totalDuration = knockbackPath.Count * movementDurationPerTile;
                    Vector3 finalFocus  = camera.WorldFocusPosition(knockbackPath[knockbackPath.Count - 1].transform.position);
                    followHandle = camera.PushFocus(finalFocus, totalDuration);
                }

                yield return new WaitForSeconds(0.1f);

                unitAnimator.PlayKnockbackMoving();

                bool endTriggered = false;

                for (int i = 0; i < knockbackPath.Count; i++)
                {
                    if (i == knockbackPath.Count - 1)
                    {
                        unitAnimator.PlayKnockbackEnd();
                        endTriggered = true;
                    }

                    bool moveComplete = false;
                    target.AnimateToTile(knockbackPath[i], movementDurationPerTile, () => moveComplete = true);
                    while (!moveComplete) yield return null;

                    if (enableCollisions)
                    {
                        Tile beyondTile = GridManager.Instance.GetTileInDirection(knockbackPath[i], resolvedDir);
                        if (beyondTile?.currentUnit != null && beyondTile.currentUnit != target)
                        {
                            yield return HandleCollision(ctx, target, beyondTile.currentUnit, resolvedDir);
                            break;
                        }
                    }
                }

                if (!endTriggered)
                    unitAnimator.PlayKnockbackEnd();

                yield return new WaitForSeconds(knockbackEndDuration);

                if (followHandle >= 0)
                    camera.PopFocus(followHandle);
            }

            if (isPlayerSelfKnockback)
                RestorePlayerStateAfterKnockback(target);
        }
        
        private Vector2Int ResolveKnockbackDir(AbilityContext ctx, Unit target)
        {
            if (applyToSelf && target == ctx.caster)
                return GridDirectionUtility.Opposite(ctx.aimDir);
            
            if (radialKnockback && ctx.caster?.currentTile != null && target.currentTile != null)
            {
                Vector2Int radialDir = GridDirectionUtility.FromTiles(ctx.caster.currentTile, target.currentTile);

                return radialDir != Vector2Int.zero ? radialDir : ctx.aimDir;
            }
            
            return ctx.aimDir;
        }

        private void ResetPlayerStateAfterKnockback(Unit target)
        {
            if (!(target is PlayerUnit)) return;

            GridManager.Instance?.SetHighlightMode(GridManager.HighlightMode.None);

            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            if (combatManager != null && combatManager.CurrentActiveUnit == target)
                combatManager.BlockAnimationForEffect();
        }

        private void RestorePlayerStateAfterKnockback(Unit target)
        {
            if (!(target is PlayerUnit)) return;

            var combatManager = Object.FindAnyObjectByType<CombatManager>();
            if (combatManager != null && combatManager.CurrentActiveUnit == target)
                combatManager.StartCoroutine(DelayedStateRestore(combatManager, target));
        }

        private IEnumerator DelayedStateRestore(CombatManager combatManager, Unit target)
        {
            yield return null;

            combatManager.ReleaseAnimationBlock();

            if (combatManager.CanMove)
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, target);

            UIEvents.OnCombatStateChanged();
            UIEvents.OnUnitMoved();
        }
        
        private List<Tile> CalculateKnockbackPath(Unit target, Vector2Int knockbackDir)
        {
            var path = new List<Tile>();
            if (target.currentTile == null)
            {
                Debug.LogWarning($"[Knockback] {target.name} has no currentTile — cannot calculate path");
                return path;
            }

            Tile originTile = target.currentTile;
            
            Tile firstTile = GridDirectionUtility.ResolveKnockbackDestination(originTile, knockbackDir);
            if (firstTile == null)
            {
                Debug.Log($"[Knockback] {target.name}: all fallback directions blocked — no movement");
                return path;
            }

            path.Add(firstTile);
            
            if (GridManager.Instance.GetYLevel(firstTile) < GridManager.Instance.GetYLevel(originTile))
            {
                Debug.Log($"[Knockback] {target.name}: knocked off ledge to {firstTile.name} — stopping");
                return path;
            }

            if (knockbackDistance <= 1) return path;
            
            Vector2Int resolvedDir = GridDirectionUtility.FromTiles(originTile, firstTile);
            Tile current = firstTile;

            for (int step = 2; step <= knockbackDistance; step++)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, resolvedDir);

                if (next == null)
                {
                    Debug.Log($"[Knockback] {target.name} step {step}: no tile in {GridDirectionUtility.ToName(resolvedDir)}");
                    break;
                }

                int currentLevel = GridManager.Instance.GetYLevel(current);
                int nextLevel    = GridManager.Instance.GetYLevel(next);

                if (nextLevel > currentLevel)
                {
                    Debug.Log($"[Knockback] {target.name} step {step}: {next.name} is higher — treating as wall");
                    break;
                }

                if (!next.passableTerrain)
                {
                    Debug.Log($"[Knockback] {target.name} step {step}: {next.name} is impassable");
                    break;
                }

                if (next.currentUnit != null && next.currentUnit != target)
                {
                    Debug.Log($"[Knockback] {target.name} step {step}: {next.name} occupied by {next.currentUnit.name}");
                    break;
                }

                bool isDropDown = nextLevel < currentLevel;
                path.Add(next);
                current = next;

                if (isDropDown)
                {
                    Debug.Log($"[Knockback] {target.name} step {step}: knocked down to {next.name} — stopping");
                    break;
                }
            }

            Debug.Log($"[Knockback] {target.name}: path = {path.Count} tile(s), primary dir = {GridDirectionUtility.ToName(knockbackDir)}");
            return path;
        }
        
        private void ApplyKnockbackImmediate(Unit target, Vector2Int knockbackDir)
        {
            Tile originTile = target.currentTile;
            if (originTile == null) return;

            Debug.Log($"[Knockback Immediate] {target.name}: no UnitAnimator — applying instant movement. Dir = {GridDirectionUtility.ToName(knockbackDir)}");

            Tile firstTile = GridDirectionUtility.ResolveKnockbackDestination(originTile, knockbackDir);
            if (firstTile == null)
            {
                Debug.Log($"[Knockback Immediate] {target.name}: all fallback directions blocked — no movement");
                return;
            }

            Debug.Log($"[Knockback Immediate] {target.name}: moving to {firstTile.name}");

            bool firstIsDropDown =
                GridManager.Instance.GetYLevel(firstTile) < GridManager.Instance.GetYLevel(originTile);

            target.SetCurrentTile(firstTile);

            if (firstIsDropDown || knockbackDistance <= 1) return;

            Vector2Int resolvedDir = GridDirectionUtility.FromTiles(originTile, firstTile);
            Tile current = firstTile;

            for (int step = 2; step <= knockbackDistance; step++)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, resolvedDir);

                if (next == null || !next.passableTerrain ||
                    (next.currentUnit != null && next.currentUnit != target))
                    break;

                int currentLevel = GridManager.Instance.GetYLevel(current);
                int nextLevel    = GridManager.Instance.GetYLevel(next);

                if (nextLevel > currentLevel) break;

                bool isDropDown = nextLevel < currentLevel;
                target.SetCurrentTile(next);
                current = next;

                if (isDropDown) break;
            }
        }

        private IEnumerator HandleCollision(AbilityContext ctx, Unit knockingUnit, Unit collidedUnit, Vector2Int knockbackDirection)
        {
            if (!collidedUnit.IsBody)
            {
                int damage = collisionDamage >= 0 ? collisionDamage : ctx.ability.damage;
                collidedUnit.ReceiveDamage(damage);
                Debug.Log($"[Knockback] {collidedUnit.name} takes {damage} collision damage");

                if (BigMomentSequencer.Instance != null)
                    yield return BigMomentSequencer.Instance.DrainQueue();
            }
            
            Tile destination = GridDirectionUtility.ResolveKnockbackDestination(
                collidedUnit.currentTile, knockbackDirection);

            if (destination == null) yield break; 

            if (collidedUnit.IsBody)
            {
                ctx.caster.StartCoroutine(SilentBodyDisplace(collidedUnit, destination));
            }
            else
            {
                var animator = collidedUnit.GetComponent<UnitAnimator>();
                if (animator != null)
                    ctx.caster.StartCoroutine(SecondaryKnockback(collidedUnit, destination, animator));
                else
                    collidedUnit.SetCurrentTile(destination);
            }
        }
        
        private IEnumerator SilentBodyDisplace(Unit body, Tile destination)
        {
            bool done = false;
            body.AnimateToTile(destination, movementDurationPerTile, () => done = true);
            while (!done) yield return null;
        }

        private IEnumerator SecondaryKnockback(Unit unit, Tile destination, UnitAnimator animator)
        {
            animator.PlayKnockbackStart();
            yield return new WaitForSeconds(0.2f);

            bool done = false;
            unit.AnimateToTile(destination, 0.2f, () => done = true);
            while (!done) yield return null;

            animator.PlayKnockbackEnd();
            yield return new WaitForSeconds(0.2f);
        }
        
        private Unit GetDisplacedUnit(AbilityContext ctx)
        {
            if (ctx.caster?.currentTile == null) return null;

            Tile finalTile = ctx.caster.currentTile;

            foreach (var unit in UnitManager.AllUnits)
            {
                if (unit == null || unit == ctx.caster) continue;
                if (unit.currentTile == finalTile)
                    return unit;
            }

            return null;
        }
    }
}