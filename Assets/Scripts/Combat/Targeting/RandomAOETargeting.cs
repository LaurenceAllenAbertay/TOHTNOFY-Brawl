using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Random AOE")]
public class RandomAOETargeting : AbilityTargeting
{
    // Cache tiles as absolute references. The selection is locked in for the turn
    // and re-rolled when the caster moves (position change) or a new turn starts.
    // Cancelling and re-entering targeting does NOT re-roll.
    private List<Tile> cachedSelection = new List<Tile>();
    private Ability lastAbility;

    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        if (ctx?.ability == null || ctx.caster == null)
            return new List<Tile>();

        if (cachedSelection.Count == 0 || lastAbility != ctx.ability)
        {
            cachedSelection = GenerateRandomSelection(ctx);
            lastAbility = ctx.ability;
        }

        return new List<Tile>(cachedSelection);
    }

    /// <summary>
    /// Clears the cache so a fresh roll happens next time GetTraversal is called.
    /// Call this on turn start (all units) and when the caster moves.
    /// Do NOT call on cancel/retarget — the same tiles must persist.
    /// </summary>
    public void ResetForNewTurn()
    {
        cachedSelection.Clear();
        lastAbility = null;
    }

    private List<Tile> GenerateRandomSelection(AbilityContext ctx)
    {
        var start = ctx.caster.currentTile;
        if (start == null) return new List<Tile>();

        var pool = GetAllTilesInRange(start, ctx.ability.range);
        pool.Remove(start);

        var selectedTiles = new List<Tile>();
        int targetsToSelect = Mathf.Min(ctx.ability.maxTargets, pool.Count);

        for (int i = 0; i < targetsToSelect; i++)
        {
            int randomIndex = Random.Range(0, pool.Count);
            selectedTiles.Add(pool[randomIndex]);
            pool.RemoveAt(randomIndex);
        }

        return selectedTiles;
    }

    private List<Tile> GetAllTilesInRange(Tile startTile, int range)
    {
        var tilesInRange = new List<Tile>();

        if (affectsOverGaps)
        {
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