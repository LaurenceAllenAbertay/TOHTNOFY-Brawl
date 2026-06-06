using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/AOE")]
public class AOETargeting : AbilityTargeting
{
    public bool includeSelf = false;

    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var center = ctx.caster.currentTile;
        if (center == null) return tiles;

        // Get all tiles within radius using BFS
        var queue = new Queue<(Tile tile, int distance)>();
        var visited = new HashSet<Tile>();

        queue.Enqueue((center, 0));
        visited.Add(center);

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();

            // Include this tile if it's within range and meets inclusion criteria
            if (distance <= ctx.EffectiveRange && (distance > 0 || includeSelf))
            {
                // Only include tiles with passable terrain (unless affectsOverGaps is true)
                if (current.passableTerrain || affectsOverGaps)
                {
                    tiles.Add(current);
                }
            }

            // Continue searching neighbors if we haven't reached max range
            if (distance < ctx.EffectiveRange)
            {
                var adjacentTiles = GridManager.Instance.GetAdjacentTiles(current);

                foreach (var adjacent in adjacentTiles)
                {
                    if (adjacent == null || visited.Contains(adjacent)) continue;

                    // Check wall blocking if affectsThroughWalls is false
                    if (!affectsThroughWalls && !IsBlockedByWall(current, adjacent))
                    {
                        visited.Add(adjacent);
                        queue.Enqueue((adjacent, distance + 1));
                    }
                    else if (affectsThroughWalls)
                    {
                        // Only traverse through passable terrain that we haven't visited (unless affectsOverGaps allows)
                        if (adjacent.passableTerrain || affectsOverGaps)
                        {
                            visited.Add(adjacent);
                            queue.Enqueue((adjacent, distance + 1));
                        }
                    }
                }
            }
        }

        return tiles;
    }
}