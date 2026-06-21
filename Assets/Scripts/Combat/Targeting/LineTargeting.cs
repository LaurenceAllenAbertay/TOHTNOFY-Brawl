using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Line Targeting")]
    public class LineTargeting : AbilityTargeting
    {
        // ── Input behaviour ───────────────────────────────────────────────────────
        // Line targeting is aimed by mouse direction — preview updates on mouse move,
        // confirms on mouse click. No enter preview (direction not yet chosen).

        public override bool UsesDirectionalInput => true;
        public override bool UsesCameraTransitionsPerTarget => true;

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

            // Track which perpendicular lanes have been blocked by a wall.
            // Each lane (w offset) is independent — a wall in the centre lane
            // does not stop the outer lanes.
            bool[] laneBlocked = new bool[effectiveWidth];

            for (int i = 0; i < max; i++)
            {
                // Step forward one tile in the aim direction.
                Vector3 centerPos = start.transform.position +
                    new Vector3(dir.x * tileSpacing.x * (i + 1), 0, dir.y * tileSpacing.z * (i + 1));

                // For each row, expand perpendicular by halfWidth on each side.
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    int laneIndex = w + halfWidth;

                    // Skip this lane entirely if a wall already blocked it.
                    if (laneBlocked[laneIndex]) continue;

                    Vector3 tilePos = centerPos +
                        new Vector3(perp.x * tileSpacing.x * w, 0, perp.y * tileSpacing.z * w);

                    Tile nextTile = GridManager.Instance.GetTileAtPosition(tilePos);

                    if (nextTile == null)
                    {
                        // No tile at this position (gap).
                        // If the ability shoots over gaps, keep stepping — tiles beyond
                        // the gap are still valid targets.  If it can't, leave the lane
                        // open so a later tile at the same w-offset can still be reached
                        // (walls block lanes, gaps don't unless explicitly disallowed).
                        if (!affectsOverGaps) laneBlocked[laneIndex] = true;
                        continue;
                    }
                    else
                    {
                        // Determine the tile one step behind in this lane so we can raycast
                        // from it (or from the caster tile on the first step).
                        Tile prevTile;
                        if (i == 0)
                        {
                            prevTile = start;
                        }
                        else
                        {
                            Vector3 prevPos = start.transform.position +
                                new Vector3(dir.x * tileSpacing.x * i, 0, dir.y * tileSpacing.z * i) +
                                new Vector3(perp.x * tileSpacing.x * w, 0, perp.y * tileSpacing.z * w);
                            prevTile = GridManager.Instance.GetTileAtPosition(prevPos);
                        }

                        // If there is a wall between the previous tile and this one,
                        // block this lane for all remaining steps (unless the ability
                        // is flagged to pass through walls).
                        // prevTile can be null when the previous step was a gap (no tile
                        // registered at that grid position — e.g. a tree or wall object
                        // sitting on a non-tile space). In that case we still need to
                        // check for walls; fall back to raycasting from the caster's
                        // start tile so the projectile origin is always valid.
                        Tile raycastFrom = prevTile ?? start;
                        if (!affectsThroughWalls && IsBlockedByWall(raycastFrom, nextTile))
                        {
                            laneBlocked[laneIndex] = true;
                            continue;
                        }

                        if (!tiles.Contains(nextTile))
                        {
                            tiles.Add(nextTile);
                        }
                    }
                }
            }

            return tiles;
        }

        public override bool IsValidAimDirection(Vector2Int aimDir) => IsValidDirection(aimDir);

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
}