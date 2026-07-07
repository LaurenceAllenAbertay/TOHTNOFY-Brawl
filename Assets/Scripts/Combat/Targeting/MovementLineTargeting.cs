using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Movement Line")]
    public class MovementLineTargeting : AbilityTargeting
    {
        public override bool UsesDirectionalInput => true;
        
        public bool horizontalOnly = false;
        
        public bool stopAtFirstUnit = true;
        
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
            
            if (horizontalOnly)
            {
                if (dir == Vector2Int.up || dir == Vector2Int.down)
                    return tiles;

                if (dir != Vector2Int.left && dir != Vector2Int.right)
                    dir = Vector2Int.right; 
            }

            int max = Mathf.Max(1, ctx.EffectiveRange);

            Vector3 currentPos = start.transform.position;
            Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();
            Tile lastValidTile = null;

            for (int i = 1; i <= max; i++)
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
                
                if (!affectsThroughWalls)
                {
                    Tile checkFromTile = lastValidTile ?? start;
                    if (IsBlockedByWall(checkFromTile, nextTile))
                    {
                        break;
                    }
                }
                
                if (!nextTile.passableTerrain && !affectsOverGaps)
                {
                    break;
                }
                
                tiles.Add(nextTile);
                
                if (nextTile.passableTerrain)
                {
                    lastValidTile = nextTile;
                }
                
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

            if (requireEmptyDestination && tiles.Count > 0)
            {
                var destination = tiles[tiles.Count - 1];
                if (destination.occupied && destination.currentUnit != ctx.caster)
                    return result;
            }
            
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
                else
                {
                    break;
                }
            }

            return result;
        }
        
        public Tile GetMovementDestination(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            var tiles = GetTraversal(ctx);
            if (tiles.Count == 0) return ctx.caster.currentTile;
            
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