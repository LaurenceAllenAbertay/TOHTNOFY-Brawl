using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Random AOE")]
public class RandomAOETargeting : AbilityTargeting
{
    // Cache the last selection to prevent flickering during preview
    private List<Tile> cachedSelection = new List<Tile>();
    private Unit lastCaster;
    private Ability lastAbility;
    private bool isInPreviewMode = false;

    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        if (ctx?.ability == null || ctx.caster == null)
            return new List<Tile>();

        // Check if we need to regenerate the selection
        if (ShouldRegenerateSelection(ctx))
        {
            cachedSelection = GenerateRandomSelection(ctx);
            lastCaster = ctx.caster;
            lastAbility = ctx.ability;
            isInPreviewMode = true;
        }

        return new List<Tile>(cachedSelection); // Return a copy
    }

    private bool ShouldRegenerateSelection(AbilityContext ctx)
    {
        // Regenerate if this is the first call or if the context has meaningfully changed
        return !isInPreviewMode ||
               lastCaster != ctx.caster ||
               lastAbility != ctx.ability;

        // don't check aimDir because for random AOE, direction shouldn't matter
        // If random AOE does depend on aim direction, add that check here
    }

    /// <summary>
    /// Clears the cached selection so a fresh roll is generated next time this ability
    /// is opened for targeting. Called at the start of ANY unit's turn — not on cancel
    /// or execution — so the same tiles stay consistent for the entire turn regardless
    /// of cancel/retarget or movement.
    /// </summary>
    public void ResetForNewTurn()
    {
        isInPreviewMode = false;
        cachedSelection.Clear();
    }

    private List<Tile> GenerateRandomSelection(AbilityContext ctx)
    {
        var start = ctx.caster.currentTile;
        if (start == null) return new List<Tile>();

        // All tiles within range are valid landing spots — unit filtering is handled
        // downstream by SelectTargets. Picking from occupied-only tiles would mean
        // the spread is dictated by where enemies stand rather than being truly random.
        var pool = GetAllTilesInRange(start, ctx.ability.range);
        pool.Remove(start); // Never land on the caster's own tile

        // Randomly select up to maxTargets distinct tiles from the pool
        var selectedTiles = new List<Tile>();
        int targetsToSelect = Mathf.Min(ctx.ability.maxTargets, pool.Count);

        for (int i = 0; i < targetsToSelect; i++)
        {
            int randomIndex = Random.Range(0, pool.Count);
            selectedTiles.Add(pool[randomIndex]);
            pool.RemoveAt(randomIndex); // Remove to avoid duplicates
        }

        return selectedTiles;
    }

    private List<Tile> GetAllTilesInRange(Tile startTile, int range)
    {
        var tilesInRange = new List<Tile>();

        if (affectsOverGaps)
        {
            // When affecting over gaps, do a direct distance check against every tile in the
            // scene rather than a BFS through walkable terrain. This ensures gap tiles and
            // tiles on different Y levels are all included in the pool, since BFS via
            // GetAdjacentTiles only traverses passable XZ neighbours.
            // Y distance is included so that a tile 3 across and 2 up costs 5 range.
            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || tile == startTile) continue;
                int dist = GridManager.Instance.GetGridDistance(startTile, tile, includeYLevel: true);
                if (dist <= range)
                    tilesInRange.Add(tile);
            }
        }
        else
        {
            // BFS through passable terrain only — gaps and impassable tiles are excluded.
            var queue = new Queue<(Tile tile, int distance)>();
            var visited = new HashSet<Tile>();

            queue.Enqueue((startTile, 0));
            visited.Add(startTile);

            while (queue.Count > 0)
            {
                var (current, distance) = queue.Dequeue();

                if (distance > 0 && distance <= range)
                    tilesInRange.Add(current);

                if (distance < range)
                {
                    var adjacentTiles = GridManager.Instance.GetAdjacentTiles(current);
                    foreach (var adjacent in adjacentTiles)
                    {
                        if (adjacent != null && !visited.Contains(adjacent) && adjacent.passableTerrain)
                        {
                            visited.Add(adjacent);
                            queue.Enqueue((adjacent, distance + 1));
                        }
                    }
                }
            }
        }

        return tilesInRange;
    }
}