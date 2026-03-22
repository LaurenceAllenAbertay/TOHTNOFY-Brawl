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

    // Call this when the ability is actually executed (not just previewed)
    public void OnAbilityExecuted()
    {
        // Clear cache so next time get a fresh random selection
        isInPreviewMode = false;
        cachedSelection.Clear();
    }

    // Call this when targeting is cancelled
    public void OnTargetingCancelled()
    {
        // Clear cache so next time get a fresh random selection
        isInPreviewMode = false;
        cachedSelection.Clear();
    }

    private List<Tile> GenerateRandomSelection(AbilityContext ctx)
    {
        var start = ctx.caster.currentTile;
        if (start == null) return new List<Tile>();

        // Get all tiles within ability range
        var allTilesInRange = GetAllTilesInRange(start, ctx.ability.range);
        allTilesInRange.Remove(start); // Don't target self

        // Build pool of valid tiles based on targeting rules
        var validTiles = new List<Tile>();

        foreach (var tile in allTilesInRange)
        {
            var unit = tile.currentUnit;
            if (unit != null)
            {
                // Tile has a unit - check if we can target it
                bool isAlly = IsAlly(ctx.caster, unit);
                bool canHit = (isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies);

                if (canHit)
                {
                    validTiles.Add(tile);
                }
            }
            else if (ctx.ability.canExecuteWithoutTargets)
            {
                // Empty tile - include if ability can execute without targets
                validTiles.Add(tile);
            }
        }

        // Randomly select up to maxTargets tiles from the valid pool
        var selectedTiles = new List<Tile>();
        int targetsToSelect = Mathf.Min(ctx.ability.maxTargets, validTiles.Count);

        for (int i = 0; i < targetsToSelect; i++)
        {
            var randomIndex = Random.Range(0, validTiles.Count);
            var selectedTile = validTiles[randomIndex];
            selectedTiles.Add(selectedTile);
            validTiles.RemoveAt(randomIndex); // Remove to avoid duplicates
        }

        return selectedTiles;
    }

    private List<Tile> GetAllTilesInRange(Tile startTile, int range)
    {
        var tilesInRange = new List<Tile>();
        var queue = new Queue<(Tile tile, int distance)>();
        var visited = new HashSet<Tile>();

        queue.Enqueue((startTile, 0));
        visited.Add(startTile);

        while (queue.Count > 0)
        {
            var (current, distance) = queue.Dequeue();

            if (distance <= range)
            {
                tilesInRange.Add(current);

                if (distance < range)
                {
                    // CHANGED: Use GridManager to get adjacent tiles
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