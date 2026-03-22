using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Line Targeting")]
public class LineTargeting : AbilityTargeting
{
    [Tooltip("If true, this ability can only be aimed left or right (horizontal only)")]
    public bool horizontalOnly = true;
    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var start = ctx.caster.currentTile;
        if (start == null) return tiles;

        var dir = ctx.aimDir;

        // Restrict direction if horizontalOnly is enabled
        if (horizontalOnly)
        {
            // If trying to aim vertically, don't execute the ability
            if (dir == Vector2Int.up || dir == Vector2Int.down)
            {
                return tiles; // Return empty list - ability won't execute
            }

            // Ensure we only have left or right direction
            if (dir != Vector2Int.left && dir != Vector2Int.right)
            {
                // Default to right if somehow we get here with an invalid direction
                dir = Vector2Int.right;
            }
        }

        int max = Mathf.Max(1, ctx.ability.range);

        Vector3 currentPos = start.transform.position;
        Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

        for (int i = 0; i < max; i++)
        {
            currentPos += new Vector3(dir.x * tileSpacing.x, 0, dir.y * tileSpacing.z);
            Tile nextTile = GridManager.Instance.GetTileAtPosition(currentPos);

            if (nextTile == null)
            {
                if (!affectsOverGaps)
                {
                    break;
                }
                continue;
            }

            if (!affectsThroughWalls && tiles.Count > 0)
            {
                Tile lastTile = tiles[tiles.Count - 1];
                if (IsBlockedByWall(lastTile, nextTile))
                {
                    break;
                }
            }
            else if (!affectsThroughWalls && tiles.Count == 0)
            {
                if (IsBlockedByWall(start, nextTile))
                {
                    break;
                }
            }

            tiles.Add(nextTile);

            if (nextTile.currentUnit != null && !ctx.ability.passThroughUnits)
            {
                break;
            }

            if (!nextTile.passableTerrain && !affectsOverGaps)
            {
                break;
            }
        }

        return tiles;
    }

    /// <summary>
    /// Checks if the given direction is valid for this targeting type
    /// </summary>
    public bool IsValidDirection(Vector2Int direction)
    {
        if (!horizontalOnly)
            return true; // All directions allowed

        // Only left and right are valid when horizontalOnly is true
        return direction == Vector2Int.left || direction == Vector2Int.right;
    }
}