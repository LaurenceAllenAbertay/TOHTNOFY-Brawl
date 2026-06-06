using DDD.TNFY.BRAWL;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Effects/Knockback Effect")]
public class KnockbackEffect : AbilityEffect
{
    [Tooltip("How far to push the target")]
    public int knockbackDistance = 1;

    [Tooltip("If true, this knockback only affects the final target")]
    public bool finalTargetOnly = false;

    [Tooltip("If true, units knocked into other units will cause collision damage and secondary knockback")]
    public bool enableCollisions = false;

    [Tooltip("Damage dealt to units hit by collision (if different from main ability damage)")]
    public int collisionDamage = -1; // -1 means use ability damage

    [Tooltip("If true, make sure the unit moves to a new tile")]
    public bool forceKnockback = true;

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

    public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        if (ctx?.caster == null) return;

        // Camera follow only when a single unit is launched over multiple tiles.
        bool singleTarget = (targets == null || targets.Count == 1) && !applyToSelf;
        bool shouldFollowCamera = singleTarget && knockbackDistance > 1;

        if (applyToSelf)
        {
            // Apply knockback to the caster
            ctx.caster.StartCoroutine(ApplyKnockbackWithAnimation(ctx, ctx.caster, false));
        }
        else if (finalTargetOnly)
        {
            Unit displacedUnit = GetDisplacedUnit(ctx);
            if (displacedUnit != null)
            {
                ctx.caster.StartCoroutine(ApplyKnockbackWithAnimation(ctx, displacedUnit, shouldFollowCamera));
            }
        }
        else
        {
            if (targets != null)
            {
                foreach (var target in targets)
                {
                    if (target != null)
                        ctx.caster.StartCoroutine(ApplyKnockbackWithAnimation(ctx, target, shouldFollowCamera));
                }
            }
        }
    }

    private IEnumerator ApplyKnockbackWithAnimation(AbilityContext ctx, Unit target, bool followCamera = false)
    {
        if (target?.currentTile == null) yield break;

        var unitAnimator = target.GetComponent<UnitAnimator>();
        if (unitAnimator == null)
        {
            ApplyKnockbackImmediate(ctx, target);
            yield break;
        }

        // Determine knockback direction
        Vector2Int knockbackDir;
        bool isPlayerSelfKnockback = false;

        if (applyToSelf && target == ctx.caster)
        {
            // For self-knockback, use opposite of aim direction (knock backwards)
            knockbackDir = new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y);

            isPlayerSelfKnockback = target is PlayerUnit;
            if (isPlayerSelfKnockback)
            {
                // Block player input during animation
                ResetPlayerStateAfterKnockback(target);
            }
        }
        else if (radialKnockback && ctx.caster?.currentTile != null && target.currentTile != null)
        {
            // Radial knockback: push each target away from the caster's tile.
            // Direction is derived per-target so units scatter outward from the epicentre.
            Vector3 diff = target.currentTile.transform.position -
                           ctx.caster.currentTile.transform.position;
            int dx = diff.x > 0.01f ? 1 : (diff.x < -0.01f ? -1 : 0);
            int dz = diff.z > 0.01f ? 1 : (diff.z < -0.01f ? -1 : 0);
            // Prefer the dominant axis so diagonal targets get a clean cardinal push.
            if (Mathf.Abs(diff.x) >= Mathf.Abs(diff.z))
                knockbackDir = new Vector2Int(dx, 0);
            else
                knockbackDir = new Vector2Int(0, dz);
        }
        else
        {
            // For normal knockback, use aim direction
            knockbackDir = ctx.aimDir;
        }

        // Calculate the path first
        var knockbackPath = CalculateKnockbackPath(ctx, target, knockbackDir);

        // Face the target toward the source of the knockback (inverse of push direction).
        target.FaceDirection(new Vector2Int(-knockbackDir.x, -knockbackDir.y));

        if (!forceKnockback && knockbackPath.Count == 0)
        {
            // Restore player state if this was a player self-knockback
            if (isPlayerSelfKnockback)
            {
                RestorePlayerStateAfterKnockback(target);
            }
            yield break;
        }

        if (knockbackPath.Count == 0)
        {
            // Still play animation even if no movement for visual feedback
            unitAnimator.PlayKnockbackStart();
            yield return new WaitForSeconds(knockbackStartDuration);
            unitAnimator.PlayKnockbackEnd();
            yield return new WaitForSeconds(knockbackEndDuration);

            // Restore player state if this was a player self-knockback
            if (isPlayerSelfKnockback)
            {
                RestorePlayerStateAfterKnockback(target);
            }
            yield break;
        }

        // NEW: Start knockback animation and movement simultaneously
        unitAnimator.PlayKnockbackStart();

        if (knockbackPath.Count == 1)
        {
            // Start movement after a very brief delay to let animation begin
            yield return new WaitForSeconds(0.1f);

            // Move to destination
            bool movementComplete = false;
            target.AnimateToTile(knockbackPath[0], movementDurationPerTile, () => movementComplete = true);

            while (!movementComplete)
            {
                yield return null;
            }

            // Check for a unit on the tile immediately beyond the landing spot.
            // The launched unit now occupies knockbackPath[0], so any blocker
            // is one step further in the knockback direction.
            if (enableCollisions)
            {
                Tile beyondTile = GridManager.Instance.GetTileInDirection(knockbackPath[0], knockbackDir);
                if (beyondTile != null && beyondTile.currentUnit != null && beyondTile.currentUnit != target)
                    HandleCollision(ctx, target, beyondTile.currentUnit, knockbackDir);
            }

            // Wait for remaining start animation time, then play end
            float remainingStartTime = knockbackStartDuration - 0.1f - movementDurationPerTile;
            if (remainingStartTime > 0)
            {
                yield return new WaitForSeconds(remainingStartTime);
            }

            // Play Knockback_End
            unitAnimator.PlayKnockbackEnd();

            // Wait for Knockback_End to complete
            yield return new WaitForSeconds(knockbackEndDuration);
        }
        else
        {
            // Multi-tile knockback: Start movement immediately and hold start animation
            Debug.Log($"Multi-tile knockback for {target.name} - {knockbackPath.Count} tiles");

            // Cache camera once before the loop.
            var camera = followCamera ? UnityEngine.Object.FindAnyObjectByType<CameraController>() : null;

            // Fire one smooth camera transition covering the full movement distance.
            // Duration matches total movement time so the camera arrives with the unit.
            if (camera != null)
            {
                float totalDuration = knockbackPath.Count * movementDurationPerTile;
                Vector3 finalFocus = camera.WorldFocusPosition(knockbackPath[knockbackPath.Count - 1].transform.position);
                ctx.caster.StartCoroutine(camera.TransitionTo(finalFocus, totalDuration));
            }

            // Brief delay to let knockback start animation begin
            yield return new WaitForSeconds(0.1f);

            // Move through all tiles while holding the start animation
            float timePerTile = movementDurationPerTile;

            for (int i = 0; i < knockbackPath.Count; i++)
            {
                bool moveComplete = false;
                target.AnimateToTile(knockbackPath[i], timePerTile, () => moveComplete = true);

                Debug.Log($"{target.name} knocked back to {knockbackPath[i].name} (step {i + 1}/{knockbackPath.Count})");

                while (!moveComplete)
                {
                    yield return null;
                }

                // Check for a unit on the tile immediately beyond this step.
                if (enableCollisions)
                {
                    Tile beyondTile = GridManager.Instance.GetTileInDirection(knockbackPath[i], knockbackDir);
                    if (beyondTile != null && beyondTile.currentUnit != null && beyondTile.currentUnit != target)
                    {
                        HandleCollision(ctx, target, beyondTile.currentUnit, knockbackDir);
                        break;
                    }
                }
            }

            // Calculate remaining start animation time
            float totalMovementTime = knockbackPath.Count * timePerTile;
            float remainingStartTime = knockbackStartDuration - 0.1f - totalMovementTime;
            if (remainingStartTime > 0)
            {
                yield return new WaitForSeconds(remainingStartTime);
            }

            // Now play Knockback_End
            Debug.Log($"Playing Knockback_End for {target.name}");
            unitAnimator.PlayKnockbackEnd();

            // Wait for Knockback_End to complete
            yield return new WaitForSeconds(knockbackEndDuration);
        }

        // Restore player state if this was a player self-knockback
        if (isPlayerSelfKnockback)
        {
            RestorePlayerStateAfterKnockback(target);
        }
    }

    // Restore player state if this was a player

    private void ResetPlayerStateAfterKnockback(Unit target)
    {
        // Only reset state for player units
        if (!(target is PlayerUnit)) return;

        // Clear tile highlights immediately
        if (GridManager.Instance != null)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);
        }

        // Find combat manager to block input during animation
        var combatManager = Object.FindAnyObjectByType<CombatManager>();
        if (combatManager != null && combatManager.CurrentActiveUnit == target)
        {
            // UPDATED: Set state to ExecutingAction to block input more comprehensively
            combatManager.currentState = CombatState.ExecutingAction;

            // Use reflection to set the private isWaitingForAnimation field
            var combatManagerType = typeof(CombatManager);
            var isWaitingForAnimationField = combatManagerType.GetField("isWaitingForAnimation",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (isWaitingForAnimationField != null)
            {
                isWaitingForAnimationField.SetValue(combatManager, true);
            }
        }
    }

    private void RestorePlayerStateAfterKnockback(Unit target)
    {
        // Only restore state for player units
        if (!(target is PlayerUnit)) return;

        var combatManager = Object.FindAnyObjectByType<CombatManager>();
        if (combatManager != null && combatManager.CurrentActiveUnit == target)
        {
            // UPDATED: Add a small delay to ensure animation is fully complete
            combatManager.StartCoroutine(DelayedStateRestore(combatManager, target));
        }
    }

    /// <summary>
    /// NEW: Delayed state restoration to ensure knockback animation is completely finished
    /// </summary>
    private IEnumerator DelayedStateRestore(CombatManager combatManager, Unit target)
    {
        // Wait an extra frame to ensure animation state is fully updated
        yield return null;

        // Restore input state AND clear animation flag
        combatManager.currentState = CombatState.WaitingForInput;

        // Use reflection to clear the private isWaitingForAnimation field
        var combatManagerType = typeof(CombatManager);
        var isWaitingForAnimationField = combatManagerType.GetField("isWaitingForAnimation",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (isWaitingForAnimationField != null)
        {
            isWaitingForAnimationField.SetValue(combatManager, false);
        }

        // Update movement highlights from the new position if player can still move
        if (combatManager.CanMove)
        {
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, target);
        }

        // Trigger UI updates to reflect new state
        UIEvents.OnCombatStateChanged();
        UIEvents.OnUnitMoved();
    }

    private List<Tile> CalculateKnockbackPath(AbilityContext ctx, Unit target, Vector2Int knockbackDir)
    {
        var path = new List<Tile>();
        Tile currentTile = target.currentTile;

        for (int step = 1; step <= knockbackDistance; step++)
        {
            Tile nextTile;

            nextTile = GridManager.Instance.GetTileInDirection(currentTile, knockbackDir);

            // Stop at grid edge or impassable terrain.
            if (nextTile == null || !nextTile.passableTerrain)
                break;

            // Stop if the next tile has a unit on it.
            // Always stop movement regardless of collision setting --
            // enableCollisions only controls whether damage is dealt, not whether we stop.
            if (nextTile.currentUnit != null && nextTile.currentUnit != target)
                break;

            path.Add(nextTile);
            currentTile = nextTile;
        }

        return path;
    }

    private void ApplyKnockbackImmediate(AbilityContext ctx, Unit target)
    {
        Vector2Int knockbackDir = ctx.aimDir;

        for (int step = 1; step <= knockbackDistance; step++)
        {
            Tile nextTile;

            if (forceKnockback)
            {
                // Original behavior - try alternative directions if primary is blocked
                nextTile = FindValidKnockbackTile(target.currentTile, knockbackDir);
            }
            else
            {
                // New behavior - only move in exact knockback direction
                nextTile = GridManager.Instance.GetTileInDirection(target.currentTile, knockbackDir);

                // Validate the tile is actually moveable
                if (nextTile == null || !nextTile.passableTerrain || nextTile.occupied)
                {
                    nextTile = null;
                }
            }

            if (nextTile == null)
            {
                if (step == 1) // Couldn't move at all
                {
                    Debug.Log("Can't move unit anywhere!");
                }
                break;
            }

            target.SetCurrentTile(nextTile);
        }
    }

    private Tile FindValidKnockbackTile(Tile fromTile, Vector2Int primaryDirection)
    {
        Vector2Int[] directionsToTry = GetKnockbackDirectionsPriority(primaryDirection);

        foreach (var direction in directionsToTry)
        {
            Tile targetTile = GridManager.Instance.GetTileInDirection(fromTile, direction);

            if (targetTile != null && targetTile.passableTerrain && !targetTile.occupied)
            {
                return targetTile;
            }
        }

        return null;
    }

    private void HandleCollision(AbilityContext ctx, Unit knockingUnit, Unit collidedUnit, Vector2Int knockbackDirection)
    {
        // Deal collision damage
        int damageAmount = collisionDamage >= 0 ? collisionDamage : ctx.ability.damage;
        collidedUnit.ReceiveDamage(damageAmount);
        Debug.Log($"{collidedUnit.name} takes {damageAmount} collision damage!");

        // Knockback the collided unit by 1 tile
        Tile newDestination = GridManager.Instance.GetTileInDirection(collidedUnit.currentTile, knockbackDirection);

        if (newDestination != null && newDestination.passableTerrain && !newDestination.occupied)
        {
            var collidedAnimator = collidedUnit.GetComponent<UnitAnimator>();
            if (collidedAnimator != null)
            {
                ctx.caster.StartCoroutine(SecondaryKnockback(collidedUnit, newDestination, collidedAnimator));
            }
            else
            {
                collidedUnit.SetCurrentTile(newDestination);
            }
        }
    }

    private IEnumerator SecondaryKnockback(Unit unit, Tile destination, UnitAnimator animator)
    {
        // Quick knockback for collision
        animator.PlayKnockbackStart();
        yield return new WaitForSeconds(0.2f);

        bool moveComplete = false;
        unit.AnimateToTile(destination, 0.2f, () => moveComplete = true);

        while (!moveComplete)
        {
            yield return null;
        }

        animator.PlayKnockbackEnd();
        yield return new WaitForSeconds(0.2f);
    }

    private Unit GetDisplacedUnit(AbilityContext ctx)
    {
        if (ctx.caster?.currentTile == null) return null;

        Tile finalTile = ctx.caster.currentTile;
        Vector3 finalTilePosition = finalTile.transform.position;

        foreach (var unit in UnitManager.AllUnits)
        {
            if (unit == null || unit == ctx.caster) continue;

            if (unit.currentTile == finalTile ||
                Vector3.Distance(unit.transform.position, finalTilePosition) < 0.5f)
            {
                return unit;
            }
        }

        return null;
    }

    private Vector2Int[] GetKnockbackDirectionsPriority(Vector2Int primaryDirection)
    {
        Vector2Int backwards = primaryDirection;
        Vector2Int left = GetPerpendicularDirection(primaryDirection, false);
        Vector2Int right = GetPerpendicularDirection(primaryDirection, true);

        return new Vector2Int[]
        {
            backwards,
            left,
            right
        };
    }

    private Vector2Int GetPerpendicularDirection(Vector2Int direction, bool clockwise)
    {
        if (clockwise)
            return new Vector2Int(-direction.y, direction.x);
        else
            return new Vector2Int(direction.y, -direction.x);
    }

    private string GetDirectionName(Vector2Int direction)
    {
        if (direction == Vector2Int.up) return "north";
        if (direction == Vector2Int.down) return "south";
        if (direction == Vector2Int.left) return "west";
        if (direction == Vector2Int.right) return "east";
        return $"({direction.x},{direction.y})";
    }
}