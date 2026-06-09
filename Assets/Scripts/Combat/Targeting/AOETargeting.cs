using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/AOE")]
    public class AOETargeting : AbilityTargeting
    {
        // ── Input behaviour ───────────────────────────────────────────────────────
        // AOE is aimed by mouse direction — preview updates on mouse move,
        // confirms on mouse click. No enter preview (direction not yet chosen).

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
                // When reaching tiles on other Y levels we can't BFS via GetAdjacentTiles
                // because that helper is restricted to same-Y neighbours. Instead iterate
                // every tile and use IsAllowedByLayerFlags, which:
                //   • rejects tiles above  if affectsUpperLayers is false
                //   • rejects tiles below  if affectsLowerLayers is false
                //   • charges full 3D Manhattan distance (X + Z + Y steps) so a tile
                //     that is 1 across and 1 down costs 2 range, not 1.
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
                // No cross-layer reach — same-Y BFS, unchanged from original behaviour.
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