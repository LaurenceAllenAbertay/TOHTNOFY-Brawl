using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Random AOE")]
    public class RandomAOETargeting : AbilityTargeting
    {
        public override bool UsesDirectionalInput => false;
        public override bool ConfirmsOnTileClick  => true;

        public override void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile)
        {
            ShowTraversalPreview(ctx);
        }


        public override bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
        {
            outCtx = null;
            return true;
        }
        
        private List<Tile> cachedSelection = new List<Tile>();
        private Unit lastCaster;
        private Ability lastAbility;
        private bool isInPreviewMode = false;

        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            if (ctx?.ability == null || ctx.caster == null)
                return new List<Tile>();
            
            if (ShouldRegenerateSelection(ctx))
            {
                cachedSelection = GenerateRandomSelection(ctx);
                lastCaster = ctx.caster;
                lastAbility = ctx.ability;
                isInPreviewMode = true;
            }

            return new List<Tile>(cachedSelection); 
        }

        private bool ShouldRegenerateSelection(AbilityContext ctx)
        {
            return !isInPreviewMode ||
                   lastCaster != ctx.caster ||
                   lastAbility != ctx.ability;
        }

        public override void ResetForNewTurn()
        {
            isInPreviewMode = false;
            cachedSelection.Clear();
        }

        private List<Tile> GenerateRandomSelection(AbilityContext ctx)
        {
            var start = ctx.caster.currentTile;
            if (start == null) return new List<Tile>();
            
            var pool = GetAllTilesInRange(start, ctx.EffectiveRange);
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

            if (affectsOverGaps || affectsUpperLayers || affectsLowerLayers)
            {
                foreach (var tile in GridManager.Instance.AllTiles)
                {
                    if (tile == null || tile == startTile) continue;
                    if (!tile.passableTerrain && !affectsOverGaps) continue;

                    if (!IsAllowedByLayerFlags(startTile, tile, out int dist)) continue;
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
}