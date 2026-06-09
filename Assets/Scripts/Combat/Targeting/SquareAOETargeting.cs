using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Square AOE")]
    public class SquareAOETargeting : AbilityTargeting
    {
        // ── Input behaviour ───────────────────────────────────────────────────────

        public override bool UsesDirectionalInput => false;
        public override bool ConfirmsOnTileClick  => true;

        // ── Preview ───────────────────────────────────────────────────────────────

        public override void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile)
        {
            ShowTraversalPreview(ctx);
        }

        // ── Confirmation ──────────────────────────────────────────────────────────

        public override bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
        {
            // SquareAOE always targets the area around the caster — confirm on any tile click.
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

                // XZ square bounds — always required regardless of layer mode.
                if (Mathf.Abs(gridX) > range || Mathf.Abs(gridZ) > range) continue;

                if (needsLayerCheck)
                {
                    // With layer flags active, IsAllowedByLayerFlags gates direction and
                    // returns the 3D Manhattan distance so Y cost eats into the range budget.
                    if (!IsAllowedByLayerFlags(center, tile, out int dist)) continue;
                    if (dist > range) continue;
                }
                else
                {
                    // No cross-layer reach — reject anything not on the same Y level.
                    if (!GridManager.Instance.IsSameYLevel(center, tile)) continue;
                }

                if (!affectsThroughWalls && tile != center && IsBlockedByWall(center, tile)) continue;

                tiles.Add(tile);
            }

            return tiles;
        }
    }
}