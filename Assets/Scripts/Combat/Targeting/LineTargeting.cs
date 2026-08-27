using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Line Targeting")]
    public class LineTargeting : AbilityTargeting
    {
        public override bool UsesDirectionalInput => true;
        
        public bool horizontalOnly = true;
        
        public int width = 1;

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
                {
                    return tiles;
                }
                
                if (dir != Vector2Int.left && dir != Vector2Int.right)
                {
                    dir = Vector2Int.right;
                }
            }

            int max = Mathf.Max(1, ctx.EffectiveRange);

            Vector2Int perp = new Vector2Int(-dir.y, dir.x);

            int effectiveWidth = Mathf.Max(1, width % 2 == 0 ? width - 1 : width);
            int halfWidth = effectiveWidth / 2;

            Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

            bool[] laneBlocked = new bool[effectiveWidth];

            for (int i = 0; i < max; i++)
            {
                Vector3 centerPos = start.transform.position +
                    new Vector3(dir.x * tileSpacing.x * (i + 1), 0, dir.y * tileSpacing.z * (i + 1));
                
                for (int w = -halfWidth; w <= halfWidth; w++)
                {
                    int laneIndex = w + halfWidth;
                    
                    if (laneBlocked[laneIndex]) continue;

                    Vector3 tilePos = centerPos +
                        new Vector3(perp.x * tileSpacing.x * w, 0, perp.y * tileSpacing.z * w);

                    Tile nextTile = GridManager.Instance.GetTileAtPosition(tilePos);

                    if (nextTile == null)
                    {
                        if (!affectsOverGaps) laneBlocked[laneIndex] = true;
                        continue;
                    }
                    else
                    {
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
        
        public override Vector2Int ResolveAimDirection(Vector3 rawDir)
        {
            if (!horizontalOnly)
                return base.ResolveAimDirection(rawDir);
            
            return rawDir.x >= 0 ? Vector2Int.right : Vector2Int.left;
        }
        
        public bool IsValidDirection(Vector2Int direction)
        {
            if (!horizontalOnly)
                return true; 
            
            return direction == Vector2Int.left || direction == Vector2Int.right;
        }
    }
}