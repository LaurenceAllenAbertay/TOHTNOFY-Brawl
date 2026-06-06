using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Single Target")]
public class SingleTargeting : AbilityTargeting
{
    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var start = ctx.caster.currentTile;
        if (start == null) return tiles;

        // For single targeting, we expect ctx to contain the target tile
        if (ctx.targetTile != null)
        {
            // Check if the target tile is within range using simple distance calculation
            if (IsWithinRange(ctx, ctx.targetTile))
            {
                tiles.Add(ctx.targetTile);
            }
        }

        return tiles;
    }

    public List<Tile> GetTilesInRange(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var start = ctx.caster.currentTile;
        if (start == null) return tiles;

        // Get all tiles from the grid manager
        var allTiles = GridManager.Instance.AllTiles;

        foreach (var tile in allTiles)
        {
            if (tile == start) continue; // Skip the caster's tile

            // Use GridManager's existing method to calculate grid distance
            int distance = GridManager.Instance.GetGridDistance(start, tile);

            if (distance <= ctx.EffectiveRange && distance > 0)
            {
                // Add tile to valid targets regardless of terrain when affectsOverGaps is true
                if (affectsOverGaps || tile.passableTerrain)
                {
                    // Check wall blocking if affectsThroughWalls is false
                    bool blocked = false;
                    if (!affectsThroughWalls)
                    {
                        blocked = IsBlockedByWall(start, tile);
                    }

                    if (!blocked)
                    {
                        tiles.Add(tile);
                    }
                }
            }
        }

        return tiles;
    }

    public bool IsWithinRange(AbilityContext ctx, Tile targetTile)
    {
        if (ctx?.ability == null || ctx.caster == null || targetTile == null) return false;

        var start = ctx.caster.currentTile;
        if (start == null) return false;
        if (start == targetTile) return false; // Can't target self

        // Use GridManager's existing method to calculate grid distance
        int distance = GridManager.Instance.GetGridDistance(start, targetTile);

        if (distance > ctx.EffectiveRange || distance <= 0)
        {
            return false;
        }

        // Check if we can target this tile based on terrain
        if (!affectsOverGaps && !targetTile.passableTerrain)
        {
            return false;
        }

        // Check wall blocking if affectsThroughWalls is false
        if (!affectsThroughWalls && IsBlockedByWall(start, targetTile))
        {
            return false;
        }

        return true;
    }
}