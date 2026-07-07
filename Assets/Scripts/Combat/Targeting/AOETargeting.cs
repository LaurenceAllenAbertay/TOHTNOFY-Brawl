using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/AOE")]
    public class AOETargeting : AbilityTargeting
    {
        public override bool UsesDirectionalInput => true;

        public bool includeSelf = false;

        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            var tiles = new List<Tile>();
            if (ctx?.ability == null || ctx.caster == null) return tiles;

            var center = ctx.caster.currentTile;
            if (center == null) return tiles;

            bool needsLayerCheck = affectsUpperLayers || affectsLowerLayers;

            if (needsLayerCheck)
            {
                foreach (var tile in GridManager.Instance.AllTiles)
                {
                    if (tile == null || (!includeSelf && tile == center)) continue;
                    if (!tile.passableTerrain && !affectsOverGaps) continue;

                    if (!IsAllowedByLayerFlags(center, tile, out int dist)) continue;
                    if (dist <= 0 || dist > ctx.EffectiveRange) continue;
                    if (!affectsThroughWalls && IsBlockedByWall(center, tile)) continue;

                    tiles.Add(tile);
                }
            }
            else
            {
                var queue = new Queue<(Tile tile, int distance)>();
                var visited = new HashSet<Tile>();

                queue.Enqueue((center, 0));
                visited.Add(center);

                while (queue.Count > 0)
                {
                    var (current, distance) = queue.Dequeue();

                    if (distance <= ctx.EffectiveRange && (distance > 0 || includeSelf))
                    {
                        if (current.passableTerrain || affectsOverGaps)
                            tiles.Add(current);
                    }

                    if (distance < ctx.EffectiveRange)
                    {
                        foreach (var adjacent in GridManager.Instance.GetAdjacentTiles(current))
                        {
                            if (adjacent == null || visited.Contains(adjacent)) continue;

                            if (!affectsThroughWalls && !IsBlockedByWall(current, adjacent))
                            {
                                visited.Add(adjacent);
                                queue.Enqueue((adjacent, distance + 1));
                            }
                            else if (affectsThroughWalls)
                            {
                                if (adjacent.passableTerrain || affectsOverGaps)
                                {
                                    visited.Add(adjacent);
                                    queue.Enqueue((adjacent, distance + 1));
                                }
                            }
                        }
                    }
                }
            }

            return tiles;
        }
    }
}