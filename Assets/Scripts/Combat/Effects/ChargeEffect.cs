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
        [Tooltip("Speed of the charge movement in tiles per second")]
        public float chargeSpeed = 8f;
        [Tooltip("If true, caster moves to the end of the line")]
        public bool moveToEnd = true;
        [Tooltip("If true, caster charges until blocked")]
        public bool chargeUntilBlocked = false;

        [Header("Animation")]
        [Tooltip("Animation state to play while the caster is moving during the charge. " +
                 "Leave empty to hold whatever state the cast animation left the unit in.")]
        [SerializeField] private string moveAnimationState = "";

        [Tooltip("Animation state to play when the charge lands. Leave empty to return to Idle.")]
        [SerializeField] private string stopAnimationState = "";

        // ChargeEffect does not participate in the normal phase loop — the sequencer detects it
        // via HasChargeEffect() and routes to ExecuteChargeSequence instead.  The phase is
        // declared here so the type is self-documenting and any accidental Apply call is traceable.
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            // Apply is intentionally unused in the new execution flow.
            // Charge abilities are driven by Unit.ExecuteChargeSequence → ChargeEffect.ExecuteCharge.
            Debug.LogWarning($"ChargeEffect.Apply was called directly on {ctx?.ability?.abilityName} — this should not happen. Use ExecuteCharge via ExecuteChargeSequence.");
        }

        /// <summary>
        /// Full charge execution coroutine: moves the caster, follows the camera in real-time,
        /// triggers hurt animations on units the caster passes through, then plays the stop animation.
        /// Called by Unit.ExecuteChargeSequence; not called via Apply.
        /// </summary>
        public IEnumerator ExecuteCharge(AbilityContext ctx, IReadOnlyList<Unit> targets, UnitAnimator casterAnimator, CameraController cameraController)
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

            // Separate "where the caster physically lands" from "whether the hit fires".
            // If the destination tile is occupied, we try to displace the unit there.
            // If displacement isn't possible, the caster stops on the last free tile
            // before the target — but effects (damage, knockback, etc.) still apply.
            Unit unitToDisplace = null;
            Tile casterLandingTile = destination;

            if (destination.occupied && destination.currentUnit != caster)
            {
                unitToDisplace = destination.currentUnit;
                if (!CanDisplaceUnit(unitToDisplace, ctx.aimDir))
                {
                    // Cannot move the blocking unit aside — find the last free tile
                    // in the traversal for the caster to land on instead.
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

                    // If no free tile exists (caster already flush against the target),
                    // stay in place — but still continue so the hit fires.
                    casterLandingTile = lastFreeTile ?? caster.currentTile;
                    unitToDisplace = null; // no displacement, only the hit
                }
            }

            // Track whether the caster physically moves this charge.
            bool casterWillMove = casterLandingTile != caster.currentTile;

            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            Vector3 startPos = caster.transform.position;
            Vector3 endPos = casterLandingTile.transform.position;
            float distance = Vector3.Distance(startPos, endPos);
            float duration = casterWillMove ? distance / chargeSpeed : 0f;

            // Pre-calculate progress values at which the caster passes each target's tile
            var traversalTiles = ctx.ability.targeting.GetTraversal(ctx);
            var passProgressByTarget = new Dictionary<Unit, float>();
            if (targets != null)
            {
                for (int i = 0; i < traversalTiles.Count; i++)
                {
                    var tileUnit = traversalTiles[i].currentUnit;
                    if (tileUnit != null && tileUnit != caster && targets.Contains(tileUnit))
                    {
                        // Use the midpoint of the tile in the path so the hit fires as the caster crosses it
                        passProgressByTarget[tileUnit] = (float)(i + 0.5f) / traversalTiles.Count;
                    }
                }
            }
            var triggeredTargets = new HashSet<Unit>();

            // Disable camera controller so we can drive it manually
            if (cameraController != null) cameraController.enabled = false;

            // Capture the camera's current offset from the caster so we maintain the same
            // distance and height the camera settled at after the initial pan.
            Vector3 cameraOffset = cameraController != null
                ? cameraController.transform.position - caster.transform.position
                : new Vector3(0f, 0f, -6.5f);

            // Play the movement animation for the duration of the charge.
            // ForcePlayAnimation bypasses the knockback-sequence guard and the
            // "don't play same animation twice" check, ensuring a clean start.
            if (!string.IsNullOrEmpty(moveAnimationState) && casterAnimator != null)
                casterAnimator.ForcePlayAnimation(moveAnimationState);

            float elapsed = 0f;

            // When the caster can't move (blocked flush against the target), the movement
            // loop never runs and pass-through hits would be skipped. Fire them immediately.
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

                // Camera follows the caster using the same offset it had when the charge started
                if (cameraController != null)
                {
                    Vector3 camPos = caster.transform.position + cameraOffset;
                    cameraController.transform.position = cameraController.ClampToBounds(camPos);
                }

                // Trigger hurt effects on targets as the caster reaches their tile position
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

            // Snap to final position and restore camera control
            if (casterWillMove)
                caster.transform.position = endPos;
            if (cameraController != null) cameraController.enabled = true;

            // Play the stop animation (or return to idle if none configured)
            if (!string.IsNullOrEmpty(stopAnimationState) && casterAnimator != null)
                casterAnimator.PlayAnimation(stopAnimationState);
            else if (casterAnimator != null)
                casterAnimator.PlayIdle();

            // Displace any unit that was occupying the destination tile
            if (unitToDisplace != null)
                yield return caster.StartCoroutine(ApplyChargeDisplacementWithAnimation(unitToDisplace, ctx.aimDir));

            // Commit the caster's logical grid position now that all displacement is resolved
            caster.SetCurrentTileLogical(casterLandingTile);

            RestoreHighlightingAfterCharge(caster, combatManager, isPlayerUnit);
        }

        /// <summary>
        /// Applies a hurt animation, spawns the hit VFX, and applies damage effects to a unit
        /// the caster passes through during the charge.
        /// </summary>
        private void TriggerPassThroughHit(AbilityContext ctx, Unit target)
        {
            // Play hurt animation on the target
            target.GetComponent<UnitAnimator>()?.PlayHurt();

            // Spawn hit VFX at the target's position
            CombatVFXManager.Instance?.SpawnHitEffects(ctx, new List<Unit> { target });

            // Apply all effects that belong to the per-target hit phases.
            // Displacement effects (Knockback, Teleport, etc.) and the ChargeEffect itself
            // are intentionally excluded — displacement of pass-through units is handled
            // separately by ApplyChargeDisplacementWithAnimation on the destination unit only.
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

        /// <summary>
        /// Applies animated displacement to a unit that was hit by the charge.
        /// Bodies are moved silently — no animation is played, preserving the death pose.
        /// Living units receive a knockback-style animation for visual feedback.
        /// </summary>
        private IEnumerator ApplyChargeDisplacementWithAnimation(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace?.currentTile == null) yield break;

            // Find a valid displacement tile first — applies equally to bodies and living units.
            Tile displacementTile = FindDisplacementTile(unitToDisplace, chargeDirection);
            if (displacementTile == null)
            {
                Debug.LogWarning($"ChargeEffect: No valid displacement tile found for {unitToDisplace.name}");
                yield break;
            }

            // Bodies must never play any animation — they are frozen in their death pose.
            // Move them silently, exactly as KnockbackEffect.SilentBodyDisplace does.
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
                // Fallback to instant displacement if no animator
                DisplaceUnitInstant(unitToDisplace, chargeDirection);
                yield break;
            }

            // Check if this is a player unit to handle input blocking
            bool isPlayerUnit = unitToDisplace is PlayerUnit;
            var combatManager = Object.FindAnyObjectByType<CombatManager>();

            if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == unitToDisplace)
                combatManager.BlockAnimationForEffect();

            // Start knockback animation (same as knockback effect uses)
            unitAnimator.PlayKnockbackStart();

            // Brief delay to let animation begin
            yield return new WaitForSeconds(0.1f);

            // Animate the displacement movement
            float movementDuration = 0.3f; // Faster than normal knockback since it's a quick displacement
            bool movementComplete = false;
            unitToDisplace.AnimateToTile(displacementTile, movementDuration, () => movementComplete = true);

            while (!movementComplete)
            {
                yield return null;
            }

            // Play knockback end animation
            unitAnimator.PlayKnockbackEnd();
            yield return new WaitForSeconds(0.3f); // Shorter end duration than full knockback

            // Restore player state if this was a player unit
            if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == unitToDisplace)
            {
                yield return combatManager.StartCoroutine(RestorePlayerStateAfterDisplacement(combatManager, unitToDisplace));
            }
        }

        /// <summary>
        /// Finds a valid tile to displace the unit to, using the same logic as the existing CanDisplaceUnit method
        /// </summary>
        private Tile FindDisplacementTile(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace?.currentTile == null) return null;

            var currentTile = unitToDisplace.currentTile;
            Vector2Int[] perpendiculars = GetPerpendicularDirections(chargeDirection);
            Vector2Int backward = new Vector2Int(-chargeDirection.x, -chargeDirection.y);

            // Check perpendicular tiles first — same layer only.
            foreach (var dir in perpendiculars)
            {
                var tile = GridManager.Instance.GetTileInDirection(currentTile, dir);
                if (tile != null && tile.passableTerrain && !tile.occupied &&
                    GridManager.Instance.IsSameYLevel(currentTile, tile))
                    return tile;
            }

            // Check backward — same layer only.
            var backTile = GridManager.Instance.GetTileInDirection(currentTile, backward);
            if (backTile != null && backTile.passableTerrain && !backTile.occupied &&
                GridManager.Instance.IsSameYLevel(currentTile, backTile))
                return backTile;

            // All same-layer preferred tiles are blocked — GetAdjacentTiles already
            // enforces IsSameYLevel, so this fallback never picks an upper-layer tile.
            var adjacentTiles = GridManager.Instance.GetAdjacentTiles(currentTile);
            foreach (var tile in adjacentTiles)
            {
                if (tile.passableTerrain && !tile.occupied)
                    return tile;
            }

            return null; // No valid displacement tile found
        }

        /// <summary>
        /// Restores player state after charge displacement animation completes
        /// </summary>
        private IEnumerator RestorePlayerStateAfterDisplacement(CombatManager combatManager, Unit displacedUnit)
        {
            // Wait an extra frame to ensure animation state is fully updated
            yield return null;

            // Release the animation block — restores WaitingForInput and clears isWaitingForAnimation.
            combatManager.ReleaseAnimationBlock();

            // Update movement highlights from the new position if player can still move
            if (combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement,
                    displacedUnit,
                    movementRangeOverride: combatManager.GetRemainingMovement());
            }

            // Trigger UI updates
            UIEvents.OnCombatStateChanged();
            UIEvents.OnUnitMoved();
        }

        /// <summary>
        /// Restores appropriate tile highlighting after charge animation completes
        /// Handles both player and AI units appropriately
        /// </summary>
        private void RestoreHighlightingAfterCharge(Unit caster, CombatManager combatManager, bool isPlayerUnit)
        {
            // Only restore movement highlighting for player units during their turn
            if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == caster)
            {
                // Check if the player can still move after the charge using the correct property
                if (combatManager.CanMove)
                {
                    GridManager.Instance.SetHighlightMode(
                        GridManager.HighlightMode.Movement,
                        caster,
                        movementRangeOverride: combatManager.GetRemainingMovement());
                }
            }

            // For AI units, no highlighting is needed as they don't use visual feedback
            // The highlighting will be cleared anyway when their turn ends
        }

        private Tile CalculateDestination(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx.ability.targeting is not MovementLineTargeting lineTargeting)
                return ctx.caster.currentTile;

            // Get the traversal tiles from the targeting system
            var tiles = lineTargeting.GetTraversal(ctx);
            if (tiles.Count == 0) return ctx.caster.currentTile;

            Tile finalTile = null;

            if (chargeUntilBlocked)
            {
                // Stop at the tile before the first unit encountered
                for (int i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    if (tile.occupied && tile.currentUnit != ctx.caster)
                    {
                        // Stop at previous tile if it exists
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
                    // Prefer the last unoccupied passable tile — the charge lands there.
                    // If every tile in the traversal is occupied, pick the last passable one;
                    // the caster will stay on the tile before it (see ExecuteCharge) but the
                    // hit still fires.
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
                        // All traversal tiles are occupied — return the last passable one
                        // so ExecuteCharge knows where the hit target is.
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
                    // Move to the tile just after the last target hit
                    if (targets != null && targets.Count > 0)
                    {
                        var lastTarget = targets[targets.Count - 1];
                        int lastTargetIndex = tiles.IndexOf(lastTarget.currentTile);

                        if (lastTargetIndex >= 0 && lastTargetIndex < tiles.Count - 1)
                        {
                            // Find next passable tile after last target (can be occupied since we'll displace)
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
            Vector2Int[] perpendiculars = GetPerpendicularDirections(chargeDirection);
            Vector2Int backward = new Vector2Int(-chargeDirection.x, -chargeDirection.y);

            // Check perpendicular tiles — same layer only.
            foreach (var dir in perpendiculars)
            {
                var tile = GridManager.Instance.GetTileInDirection(currentTile, dir);
                if (tile != null && tile.passableTerrain && !tile.occupied &&
                    GridManager.Instance.IsSameYLevel(currentTile, tile))
                    return true;
            }

            // Check backward — same layer only.
            var backTile = GridManager.Instance.GetTileInDirection(currentTile, backward);
            if (backTile != null && backTile.passableTerrain && !backTile.occupied &&
                GridManager.Instance.IsSameYLevel(currentTile, backTile))
                return true;

            // GetAdjacentTiles already enforces IsSameYLevel.
            var adjacentTiles = GridManager.Instance.GetAdjacentTiles(currentTile);
            foreach (var tile in adjacentTiles)
            {
                if (!tile.occupied)
                    return true;
            }

            return false; // No displacement possible
        }

        // Updated DisplaceUnit to use instant displacement only as fallback
        private bool DisplaceUnitInstant(Unit unitToDisplace, Vector2Int chargeDirection)
        {
            if (unitToDisplace == null) return false;

            Tile displacementTile = FindDisplacementTile(unitToDisplace, chargeDirection);
            if (displacementTile == null)
            {
                // No displacement possible, destroy the unit (as in original code)
                Debug.LogWarning($"ChargeEffect: No displacement possible for {unitToDisplace.name}, destroying unit");
                unitToDisplace.ReceiveDamage(999);
                return true; // Return true because the unit is "handled" (destroyed)
            }

            // Move the unit instantly
            unitToDisplace.SetCurrentTile(displacementTile);
            Debug.Log($"ChargeEffect: Instantly displaced {unitToDisplace.name} to {displacementTile.gridPosition}");
            return true;
        }

        private Vector2Int[] GetPerpendicularDirections(Vector2Int direction)
        {
            if (direction == Vector2Int.up || direction == Vector2Int.down)
                return new Vector2Int[] { Vector2Int.left, Vector2Int.right };
            else
                return new Vector2Int[] { Vector2Int.up, Vector2Int.down };
        }
    }
}