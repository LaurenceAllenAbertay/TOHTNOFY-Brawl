using DDD.TNFY.BRAWL;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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

    public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        if (ctx?.caster == null) return;

        // Calculate the movement destination based on our settings
        Tile destination = CalculateDestination(ctx, targets);

        if (destination != null && destination != ctx.caster.currentTile)
        {
            // NEW: Clear all tile highlights before starting charge animation
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.None);

            // Start the charge movement animation - displacement will happen when we arrive
            ctx.caster.StartCoroutine(AnimateChargeMovement(ctx.caster, destination, chargeSpeed, ctx.aimDir));
        }
    }

    private IEnumerator AnimateChargeMovement(Unit caster, Tile destinationTile, float speed, Vector2Int aimDirection)
    {
        // Store reference to combat manager for state management
        var combatManager = Object.FindAnyObjectByType<CombatManager>();
        bool isPlayerUnit = caster is PlayerUnit;

        // Store the unit that might need displacement (if any)
        Unit unitToDisplace = null;
        if (destinationTile.occupied && destinationTile.currentUnit != caster)
        {
            unitToDisplace = destinationTile.currentUnit;

            // Check if displacement is possible before starting animation
            if (!CanDisplaceUnit(unitToDisplace, aimDirection))
            {
                Debug.LogWarning($"ChargeEffect: Cannot displace unit at {destinationTile.gridPosition}, charge blocked");
                // Restore highlights since we're not moving
                RestoreHighlightingAfterCharge(caster, combatManager, isPlayerUnit);
                yield break;
            }
        }

        Vector3 startPos = caster.transform.position;
        Vector3 endPos = destinationTile.transform.position;

        var cameraController = GameObject.FindAnyObjectByType<CameraController>();
        bool shouldMoveCamera = cameraController != null;

        Vector3 cameraStartPos = Vector3.zero;
        Vector3 cameraTargetPos = Vector3.zero;

        if (shouldMoveCamera)
        {
            cameraController.enabled = false;
            cameraStartPos = cameraController.transform.position;
            cameraTargetPos = new Vector3(
                endPos.x,
                cameraStartPos.y,
                endPos.z - 6.5f
            );
            cameraTargetPos = cameraController.ClampToBounds(cameraTargetPos);
        }

        float distance = Vector3.Distance(startPos, endPos);
        float duration = distance / speed;

        var unitAnimator = caster.GetComponent<UnitAnimator>();
        bool hasAnimator = unitAnimator != null;

        if (hasAnimator)
        {
            unitAnimator.PlayMove();
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float easedT = Mathf.Pow(t, 0.7f);

            caster.transform.position = Vector3.Lerp(startPos, endPos, easedT);

            if (shouldMoveCamera)
            {
                float smoothCameraT = Mathf.SmoothStep(0f, 1f, t);
                cameraController.transform.position = Vector3.Lerp(cameraStartPos, cameraTargetPos, smoothCameraT);
            }

            yield return null;
        }

        caster.transform.position = endPos;
        if (shouldMoveCamera)
        {
            cameraController.transform.position = cameraTargetPos;
            cameraController.enabled = true;
        }

        if (hasAnimator)
        {
            unitAnimator.PlayIdle();
        }

        // NEW: Apply animated displacement AFTER arriving at the destination
        if (unitToDisplace != null)
        {
            yield return caster.StartCoroutine(ApplyChargeDisplacementWithAnimation(unitToDisplace, aimDirection));
        }

        // Set the caster's logical position after displacement is handled
        caster.SetCurrentTileLogical(destinationTile);

        // Restore movement highlighting after charge completes
        RestoreHighlightingAfterCharge(caster, combatManager, isPlayerUnit);
    }

    /// <summary>
    /// Applies animated displacement to a unit that was hit by the charge
    /// Uses knockback-style animation for smooth visual feedback
    /// </summary>
    private IEnumerator ApplyChargeDisplacementWithAnimation(Unit unitToDisplace, Vector2Int chargeDirection)
    {
        if (unitToDisplace?.currentTile == null) yield break;

        var unitAnimator = unitToDisplace.GetComponent<UnitAnimator>();
        if (unitAnimator == null)
        {
            // Fallback to instant displacement if no animator
            DisplaceUnitInstant(unitToDisplace, chargeDirection);
            yield break;
        }

        // Find a valid displacement tile
        Tile displacementTile = FindDisplacementTile(unitToDisplace, chargeDirection);
        if (displacementTile == null)
        {
            Debug.LogWarning($"ChargeEffect: No valid displacement tile found for {unitToDisplace.name}");
            yield break;
        }

        // Check if this is a player unit to handle input blocking
        bool isPlayerUnit = unitToDisplace is PlayerUnit;
        var combatManager = Object.FindAnyObjectByType<CombatManager>();

        if (isPlayerUnit && combatManager != null && combatManager.CurrentActiveUnit == unitToDisplace)
        {
            // Block player input during displacement animation (similar to knockback)
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

        // Check perpendicular tiles first
        foreach (var dir in perpendiculars)
        {
            var tile = GridManager.Instance.GetTileInDirection(currentTile, dir);
            if (tile != null && tile.passableTerrain && !tile.occupied)
                return tile;
        }

        // Check backward
        var backTile = GridManager.Instance.GetTileInDirection(currentTile, backward);
        if (backTile != null && backTile.passableTerrain && !backTile.occupied)
            return backTile;

        // Check any adjacent tile as fallback
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
            GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, displacedUnit);
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
                GridManager.Instance.SetHighlightMode(GridManager.HighlightMode.Movement, caster);
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
                // Move to the very last passable tile in the traversal
                for (int i = tiles.Count - 1; i >= 0; i--)
                {
                    var tile = tiles[i];
                    if (tile.passableTerrain)
                    {
                        // NEW: Allow moving to occupied tiles since we'll displace after arriving
                        finalTile = tile;
                        break;
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

        // Check perpendicular tiles
        foreach (var dir in perpendiculars)
        {
            var tile = GridManager.Instance.GetTileInDirection(currentTile, dir);
            if (tile != null && tile.passableTerrain && !tile.occupied)
                return true;
        }

        // Check backward
        var backTile = GridManager.Instance.GetTileInDirection(currentTile, backward);
        if (backTile != null && backTile.passableTerrain && !backTile.occupied)
            return true;

        // Check any adjacent tile
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