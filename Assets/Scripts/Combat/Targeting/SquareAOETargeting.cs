using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

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

        // Get all tiles in a square pattern around the center
        var centerPos = center.transform.position;
        var range = ctx.EffectiveRange;

        foreach (var tile in GridManager.Instance.AllTiles)
        {
            if (!tile.passableTerrain && !affectsOverGaps) continue;
            if (!includeSelf && tile == center) continue;

            var tilePos = tile.transform.position;

            // Calculate grid coordinates based on tile spacing
            int gridX = Mathf.RoundToInt((tilePos.x - centerPos.x) / tileSpacing.x);
            int gridZ = Mathf.RoundToInt((tilePos.z - centerPos.z) / tileSpacing.z);

            // Check if tile is within the square bounds (using grid coordinates)
            if (Mathf.Abs(gridX) <= range && Mathf.Abs(gridZ) <= range)
            {
                // Check for wall blocking if affectsThroughWalls is false
                if (!affectsThroughWalls && tile != center)
                {
                    if (IsBlockedByWall(center, tile))
                    {
                        continue; // Skip this tile if blocked by wall
                    }
                }

                tiles.Add(tile);
            }
        }

        return tiles;
    }
}