using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Line Targeting")]
public class LineTargeting : AbilityTargeting
{
    // ── Input behaviour ───────────────────────────────────────────────────────
    // Line targeting is aimed by mouse direction — preview updates on mouse move,
    // confirms on mouse click. No enter preview (direction not yet chosen).

    public override bool UsesDirectionalInput => true;

    [Tooltip("If true, this ability can only be aimed left or right (horizontal only)")]
    public bool horizontalOnly = true;

    [Tooltip("Number of tiles wide perpendicular to the aim direction. 1 = single line (default). ")]
    public int width = 1;

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

        int max = Mathf.Max(1, ctx.EffectiveRange);

        // Perpendicular direction for width expansion.
        // For horizontal aim (left/right), perp is up/down (Z axis).
        // For vertical aim (up/down), perp is left/right (X axis).
        Vector2Int perp = new Vector2Int(-dir.y, dir.x);

        // Enforce odd width by rounding down even values.
        int effectiveWidth = Mathf.Max(1, width % 2 == 0 ? width - 1 : width);
        int halfWidth = effectiveWidth / 2;

        Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

        for (int i = 0; i < max; i++)
        {
            // Step forward one tile in the aim direction.
            Vector3 centerPos = start.transform.position +
                new Vector3(dir.x * tileSpacing.x * (i + 1), 0, dir.y * tileSpacing.z * (i + 1));

            // For each row, expand perpendicular by halfWidth on each side.
            for (int w = -halfWidth; w <= halfWidth; w++)
            {
                Vector3 tilePos = centerPos +
                    new Vector3(perp.x * tileSpacing.x * w, 0, perp.y * tileSpacing.z * w);

                Tile nextTile = GridManager.Instance.GetTileAtPosition(tilePos);

                if (nextTile == null)
                {
                    if (!affectsOverGaps) continue;
                }
                else if (!tiles.Contains(nextTile))
                {
                    tiles.Add(nextTile);
                }
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