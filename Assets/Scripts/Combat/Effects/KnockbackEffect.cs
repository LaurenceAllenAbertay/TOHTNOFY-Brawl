using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Knockback Effect")]
    public class KnockbackEffect : AbilityEffect
    {
        [Tooltip("How far to push the target")]
        public int knockbackDistance = 1;

        [Tooltip("If true, this knockback only affects the final target")]
        public bool finalTargetOnly = false;

        [Tooltip("If true, units knocked into other units will cause collision damage and secondary knockback")]
        public bool enableCollisions = false;

        [Tooltip("Damage dealt to units hit by collision (-1 = use ability damage)")]
        public int collisionDamage = -1;

        [Tooltip("If true, applies knockback to the caster instead of targets")]
        public bool applyToSelf = false;

        [Tooltip("If true, each target is knocked away from the caster rather than in ctx.aimDir.")]
        public bool radialKnockback = false;

        [Header("Animation Timing")]
        [Tooltip("Duration of the Knockback_Start animation")]
        public float knockbackStartDuration = 0.5f;

        [Tooltip("How long the movement takes per tile")]
        public float movementDurationPerTile = 0.2f;

        [Tooltip("Duration of the Knockback_End animation")]
        public float knockbackEndDuration = 0.5f;

        public override EffectAnimationPhase AnimationPhase =>
            applyToSelf ? EffectAnimationPhase.PreEffect : EffectAnimationPhase.Displacement;
        public override string TargetAnimationHint => applyToSelf ? null : "Knockback";
        public override float ExpectedAnimationDuration => 1f;

        // ── Entry point ───────────────────────────────────────────────────────

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

        // ── Primary animation coroutine ───────────────────────────────────────

        private IEnumerator ApplyKnockbackWithAnimation(AbilityContext ctx, Unit target, bool followCamera = false)
        {
            if (target?.currentTile == null) yield break;

            // Capture before any movement — needed to derive the resolved direction later.
            Tile originalTile = target.currentTile;

            // Determine push direction up-front so bodies and animator-less units share the same path.
            Vector2Int knockbackDir = ResolveKnockbackDir(ctx, target);

            // Bodies have no animator — slide them silently to their destination.
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

            // Block player input up-front when this is a self-knockback on the active player.
            bool isPlayerSelfKnockback = applyToSelf && target == ctx.caster && target is PlayerUnit;
            if (isPlayerSelfKnockback)
                ResetPlayerStateAfterKnockback(target);

            var unitAnimator = target.GetComponent<UnitAnimator>();
            if (unitAnimator == null)
            {
                ApplyKnockbackImmediate(target, knockbackDir);
                yield break;
            }

            // Build path using the full fallback chain.
            var knockbackPath = CalculateKnockbackPath(target, knockbackDir);

            // Face the target toward the source of the knockback.
            target.FaceDirection(GridDirectionUtility.Opposite(knockbackDir));

            // No valid destination — play the blocked animation in place.
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

            // The direction actually used in step 1 (may differ from knockbackDir when a fallback tile was chosen).
            Vector2Int resolvedDir = GridDirectionUtility.FromTiles(originalTile, knockbackPath[0]);

            unitAnimator.PlayKnockbackStart();

            if (knockbackPath.Count == 1)
            {
                // Single tile: brief delay then move.
                yield return new WaitForSeconds(0.1f);

                bool movementComplete = false;
                target.AnimateToTile(knockbackPath[0], movementDurationPerTile, () => movementComplete = true);
                while (!movementComplete) yield return null;

                if (enableCollisions)
                {
                    Tile beyondTile = GridManager.Instance.GetTileInDirection(knockbackPath[0], resolvedDir);
                    if (beyondTile?.currentUnit != null && beyondTile.currentUnit != target)
                        HandleCollision(ctx, target, beyondTile.currentUnit, resolvedDir);
                }

                float remainingStart = knockbackStartDuration - 0.1f - movementDurationPerTile;
                if (remainingStart > 0f)
                    yield return new WaitForSeconds(remainingStart);

                unitAnimator.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);
            }
            else
            {
                // Multi-tile: smooth camera pan then step through each tile.
                var camera = followCamera ? Object.FindAnyObjectByType<CameraController>() : null;
                if (camera != null)
                {
                    float totalDuration = knockbackPath.Count * movementDurationPerTile;
                    Vector3 finalFocus  = camera.WorldFocusPosition(knockbackPath[knockbackPath.Count - 1].transform.position);
                    ctx.caster.StartCoroutine(camera.TransitionTo(finalFocus, totalDuration));
                }

                yield return new WaitForSeconds(0.1f);

                for (int i = 0; i < knockbackPath.Count; i++)
                {
                    bool moveComplete = false;
                    target.AnimateToTile(knockbackPath[i], movementDurationPerTile, () => moveComplete = true);
                    while (!moveComplete) yield return null;

                    if (enableCollisions)
                    {
                        Tile beyondTile = GridManager.Instance.GetTileInDirection(knockbackPath[i], resolvedDir);
                        if (beyondTile?.currentUnit != null && beyondTile.currentUnit != target)
                        {
                            HandleCollision(ctx, target, beyondTile.currentUnit, resolvedDir);
                            break;
                        }
                    }
                }

                float totalMovement  = knockbackPath.Count * movementDurationPerTile;
                float remainingStart = knockbackStartDuration - 0.1f - totalMovement;
                if (remainingStart > 0f)
                    yield return new WaitForSeconds(remainingStart);

                unitAnimator.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);
            }

            if (isPlayerSelfKnockback)
                RestorePlayerStateAfterKnockback(target);
        }

        // ── Direction helper ─────────────────────────────────────────────────

        /// <summary>
        /// Determines the knockback push direction for <paramref name="target"/> based on
        /// the effect configuration and ability context.
        /// </summary>
        private Vector2Int ResolveKnockbackDir(AbilityContext ctx, Unit target)
        {
            // Self-knockback: push opposite to the aim direction (recoil).
            if (applyToSelf && target == ctx.caster)
                return GridDirectionUtility.Opposite(ctx.aimDir);

            // Radial: derive direction tile-to-tile from caster toward each target.
            // FromTiles is used rather than CardinalFromTiles so that a diagonally positioned
            // target is pushed diagonally away — CardinalFromTiles would strip the diagonal
            // and produce a cardinal direction that feeds into the wrong fallback chain.
            if (radialKnockback && ctx.caster?.currentTile != null && target.currentTile != null)
            {
                Vector2Int radialDir = GridDirectionUtility.FromTiles(ctx.caster.currentTile, target.currentTile);
                // Guard: if the two units somehow share a tile, fall back to aim direction.
                return radialDir != Vector2Int.zero ? radialDir : ctx.aimDir;
            }

            // Default: push in the ability's aim direction.
            return ctx.aimDir;
        }

        // ── Player state management ───────────────────────────────────────────

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

        // ── Path calculation ──────────────────────────────────────────────────

        /// <summary>
        /// Builds the tile path the unit will travel through.
        ///
        /// Step 1 uses the full fallback chain via
        /// <see cref="GridDirectionUtility.ResolveKnockbackDestination"/>:
        /// the unit always moves away from the source, never up a layer, and never
        /// into an occupied or impassable tile.
        ///
        /// Steps 2+ continue in the resolved direction from step 1, stopping at any
        /// wall, occupied tile, or layer boundary. A drop to a lower layer is valid
        /// but terminates the path immediately — the unit has landed.
        /// </summary>
        private List<Tile> CalculateKnockbackPath(Unit target, Vector2Int knockbackDir)
        {
            var path = new List<Tile>();
            if (target.currentTile == null)
            {
                Debug.LogWarning($"[Knockback] {target.name} has no currentTile — cannot calculate path");
                return path;
            }

            Tile originTile = target.currentTile;

            // Step 1: full fallback chain.
            Tile firstTile = GridDirectionUtility.ResolveKnockbackDestination(originTile, knockbackDir);
            if (firstTile == null)
            {
                Debug.Log($"[Knockback] {target.name}: all fallback directions blocked — no movement");
                return path;
            }

            path.Add(firstTile);

            // A drop lands the unit — stop regardless of remaining distance.
            if (GridManager.Instance.GetYLevel(firstTile) < GridManager.Instance.GetYLevel(originTile))
            {
                Debug.Log($"[Knockback] {target.name}: knocked off ledge to {firstTile.name} — stopping");
                return path;
            }

            if (knockbackDistance <= 1) return path;

            // Steps 2+: continue in the direction that step 1 actually resolved to.
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

        // ── Immediate (no-animation) fallback ─────────────────────────────────

        /// <summary>
        /// Moves the unit instantly through its knockback path when no UnitAnimator is present.
        /// Follows the same rules as the animated path.
        /// </summary>
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

        // ── Collision handling ────────────────────────────────────────────────

        private void HandleCollision(AbilityContext ctx, Unit knockingUnit, Unit collidedUnit, Vector2Int knockbackDirection)
        {
            // Bodies cannot take damage, but can still be displaced.
            if (!collidedUnit.IsBody)
            {
                int damage = collisionDamage >= 0 ? collisionDamage : ctx.ability.damage;
                collidedUnit.ReceiveDamage(damage);
                Debug.Log($"[Knockback] {collidedUnit.name} takes {damage} collision damage");
            }

            // Secondary unit follows the same full fallback chain.
            Tile destination = GridDirectionUtility.ResolveKnockbackDestination(
                collidedUnit.currentTile, knockbackDirection);

            if (destination == null) return; // All directions blocked — no secondary movement.

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

        // ── Body / secondary animation coroutines ────────────────────────────

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

        // ── Displaced unit detection ──────────────────────────────────────────

        /// <summary>
        /// Returns the unit occupying the caster's current tile (used by finalTargetOnly mode).
        /// Tile occupancy is the sole check — no world-space position comparison.
        /// </summary>
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