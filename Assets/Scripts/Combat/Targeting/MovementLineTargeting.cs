using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Movement Line")]
    public class MovementLineTargeting : AbilityTargeting
    {
        // ── Input behaviour ───────────────────────────────────────────────────────
        // Movement line (charge) is aimed by mouse direction — preview updates on mouse move,
        // confirms on mouse click. No enter preview (direction not yet chosen).

        public override bool UsesDirectionalInput => true;

        [Header("Direction")]
        [Tooltip("If true, this ability can only be aimed left or right (horizontal only)")]
        public bool horizontalOnly = false;

        [Header("Charge Behavior")]
        [Tooltip("If true, stops traversal at first occupied tile")]
        public bool stopAtFirstUnit = true;

        [Tooltip("If true, the ability cannot be used if the final tile in the traversal is occupied. " +
                 "SelectTargets returns empty when this check fails, blocking execution. " +
                 "Use for abilities like Kalpoeria where the landing spot must be free.")]
        public bool requireEmptyDestination = false;

        public override bool IsValidAimDirection(Vector2Int aimDir)
        {
            if (!horizontalOnly) return true;
            return aimDir == Vector2Int.left || aimDir == Vector2Int.right;
        }

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
                if (dir == Vector2Int.up || dir == Vector2Int.down)
                    return tiles; // Return empty list — ability won't execute vertically

                if (dir != Vector2Int.left && dir != Vector2Int.right)
                    dir = Vector2Int.right; // Fallback for unexpected directions
            }

            int max = Mathf.Max(1, ctx.EffectiveRange);

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

            // If the destination must be empty and the final tile in the path is occupied,
            // return an empty target list to block execution entirely (Ability.Execute checks
            // targets.Count == 0 && !canExecuteWithoutTargets).
            if (requireEmptyDestination && tiles.Count > 0)
            {
                var destination = tiles[tiles.Count - 1];
                if (destination.occupied && destination.currentUnit != ctx.caster)
                    return result;
            }

            // Collect all valid targets in the path
            foreach (var tile in tiles)
            {
                var unit = tile.currentUnit;
                if (unit == null || unit == ctx.caster) continue;

                bool canHit;

                if (unit.IsNeutral)
                    canHit = ctx.ability.canTargetNeutral;
                else
                {
                    bool isAlly = IsAlly(ctx.caster, unit);
                    canHit = (isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies);
                }

                if (canHit)
                {
                    result.Add(unit);

                    if (result.Count >= ctx.ability.maxTargets)
                        break;

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
}