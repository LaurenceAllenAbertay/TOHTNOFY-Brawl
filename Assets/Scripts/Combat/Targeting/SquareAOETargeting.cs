using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Square AOE")]
    public class SquareAOETargeting : AbilityTargeting
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

        public bool includeSelf = false;

        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            var tiles = new List<Tile>();
            if (ctx?.ability == null || ctx.caster == null) return tiles;

            var center = ctx.caster.currentTile;
            if (center == null) return tiles;

            Vector3 tileSpacing = MapManager.Instance.CurrentConfiguration.tileSpacing;
            var centerPos = center.transform.position;
            var range = ctx.EffectiveRange;
            bool needsLayerCheck = affectsUpperLayers || affectsLowerLayers;

            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (!tile.passableTerrain && !affectsOverGaps) continue;
                if (!includeSelf && tile == center) continue;

                var tilePos = tile.transform.position;
                int gridX = Mathf.RoundToInt((tilePos.x - centerPos.x) / tileSpacing.x);
                int gridZ = Mathf.RoundToInt((tilePos.z - centerPos.z) / tileSpacing.z);

                if (Mathf.Abs(gridX) > range || Mathf.Abs(gridZ) > range) continue;

                if (needsLayerCheck)
                {
                    if (!IsAllowedByLayerFlags(center, tile, out int dist)) continue;
                    if (dist > range) continue;
                }
                else
                {
                    if (!GridManager.Instance.IsSameYLevel(center, tile)) continue;
                }

                if (!affectsThroughWalls && tile != center && IsBlockedByWall(center, tile)) continue;

                tiles.Add(tile);
            }

            return tiles;
        }
    }
}