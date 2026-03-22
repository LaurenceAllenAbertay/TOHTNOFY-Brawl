using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Movement Line")]
public class MovementLineTargeting : AbilityTargeting
{
    [Header("Charge Behavior")]
    [Tooltip("If true, stops traversal at first occupied tile")]
    public bool stopAtFirstUnit = true;

    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var start = ctx.caster.currentTile;
        if (start == null) return tiles;

        var dir = ctx.aimDir;
        int max = Mathf.Max(1, ctx.ability.range);

        Vector3 currentPos = start.transform.position;
        Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();
        Tile lastValidTile = null;

        for (int i = 1; i <= max; i++)
        {
            // Calculate next position
            currentPos += new Vector3(dir.x * tileSpacing.x, 0, dir.y * tileSpacing.z);

            // Try to get tile at this position
            Tile nextTile = GridManager.Instance.GetTileAtPosition(currentPos);

            if (nextTile == null)
            {
                // No tile exists at this position
                if (!affectsOverGaps)
                {
                    break; // Can't continue over empty space
                }
                // Otherwise, skip this position and continue looking
                continue;
            }

            // Check if blocked by walls
            if (!affectsThroughWalls)
            {
                Tile checkFromTile = lastValidTile ?? start;
                if (IsBlockedByWall(checkFromTile, nextTile))
                {
                    break; // Wall blocks the charge
                }
            }

            // Check if we can traverse this tile
            if (!nextTile.passableTerrain && !affectsOverGaps)
            {
                break; // Can't continue past impassable terrain
            }

            // Add the tile to traversal
            tiles.Add(nextTile);

            // Update last valid tile for wall checking
            if (nextTile.passableTerrain)
            {
                lastValidTile = nextTile;
            }

            // Stop at first occupied tile if configured to do so
            if (stopAtFirstUnit && nextTile.occupied && nextTile.currentUnit != ctx.caster)
            {
                break;
            }
        }

        return tiles;
    }

    public override List<Unit> SelectTargets(AbilityContext ctx)
    {
        var result = new List<Unit>();
        if (ctx?.ability == null || ctx.caster == null) return result;

        var tiles = GetTraversal(ctx);

        // Collect all valid targets in the path
        foreach (var tile in tiles)
        {
            var unit = tile.currentUnit;
            if (unit == null || unit == ctx.caster) continue;

            bool isAlly = IsAlly(ctx.caster, unit);
            bool canHit = (isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies);

            if (canHit)
            {
                result.Add(unit);

                // Stop if we've hit max targets
                if (result.Count >= ctx.ability.maxTargets)
                    break;

                // Stop at first unit if configured to do so
                if (stopAtFirstUnit)
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Calculate where the caster should move to based on charge settings.
    /// This is called by ChargeEffect to determine the movement destination.
    /// </summary>
    public Tile GetMovementDestination(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        // Note: The ChargeEffect will have its own chargeUntilBlocked and moveToEnd settings
        // We just provide the traversal tiles and let the effect decide where to stop
        var tiles = GetTraversal(ctx);
        if (tiles.Count == 0) return ctx.caster.currentTile;

        // For now, just return the last valid passable tile in the traversal
        // The ChargeEffect should handle the actual logic based on its settings
        for (int i = tiles.Count - 1; i >= 0; i--)
        {
            if (tiles[i].passableTerrain && !tiles[i].occupied)
            {
                return tiles[i];
            }
        }

        return ctx.caster.currentTile;
    }
}