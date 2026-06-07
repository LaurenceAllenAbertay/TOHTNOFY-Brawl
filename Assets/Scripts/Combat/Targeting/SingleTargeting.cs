using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Single Target")]
public class SingleTargeting : AbilityTargeting
{
    // ── Input behaviour ───────────────────────────────────────────────────────

    public override bool UsesDirectionalInput => false;
    public override bool UsesHoverTracking    => true;
    public override bool ConfirmsOnTileClick  => true;

    // ── Preview ───────────────────────────────────────────────────────────────

    public override void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile)
    {
        var tilesInRange = GetTilesInRange(ctx);

        GridManager.Instance.ClearAllHighlights();
        foreach (var tile in tilesInRange)
            tile.Highlight(TileHighlightType.Danger);

        HighlightHoveredTile(ctx, hoveredTile, tilesInRange);
    }

    public override void ShowHoverPreview(AbilityContext ctx, Tile hoveredTile)
    {
        // Re-draw the full preview so the hover highlight updates correctly.
        ShowEnterPreview(ctx, hoveredTile);
    }

    private void HighlightHoveredTile(AbilityContext ctx, Tile hoveredTile, List<Tile> tilesInRange)
    {
        if (hoveredTile == null || !tilesInRange.Contains(hoveredTile)) return;

        bool requiresEmpty = ctx.ability.effects.Any(e => e.RequiresEmptyTargetTile);

        if (requiresEmpty)
        {
            if (hoveredTile.currentUnit == null && hoveredTile.passableTerrain)
                hoveredTile.Highlight(TileHighlightType.AttackRange);
        }
        else if (hoveredTile.currentUnit != null)
        {
            bool isAlly = hoveredTile.currentUnit is EnemyUnit == ctx.caster is EnemyUnit;
            bool canHit = (isAlly && ctx.ability.canHitAllies) ||
                          (!isAlly && ctx.ability.canHitEnemies);
            if (canHit) hoveredTile.Highlight(TileHighlightType.AttackRange);
        }
    }

    // ── Confirmation ──────────────────────────────────────────────────────────

    public override bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
    {
        outCtx = null;
        if (tile == null) return false;

        if (!IsWithinRange(ctx, tile))
        {
            Debug.Log("Target tile is out of range. Try again.");
            return false;
        }

        bool requiresEmpty = ctx.ability.effects.Any(e => e.RequiresEmptyTargetTile);
        if (requiresEmpty && (tile.occupied || !tile.passableTerrain))
        {
            Debug.Log("Cannot target occupied or impassable tile. Try again.");
            return false;
        }

        outCtx = new AbilityContext
        {
            caster     = ctx.caster,
            ability    = ctx.ability,
            targetTile = tile
        };
        return true;
    }

    // ── Range query ───────────────────────────────────────────────────────────

    public List<Tile> GetTilesInRange(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var start = ctx.caster.currentTile;
        if (start == null) return tiles;

        foreach (var tile in GridManager.Instance.AllTiles)
        {
            if (tile == start) continue;

            int distance = GridManager.Instance.GetGridDistance(start, tile);
            if (distance <= ctx.EffectiveRange && distance > 0)
            {
                if (affectsOverGaps || tile.passableTerrain)
                {
                    if (!affectsThroughWalls && IsBlockedByWall(start, tile)) continue;
                    tiles.Add(tile);
                }
            }
        }

        return tiles;
    }

    public override List<Tile> GetTraversal(AbilityContext ctx)
    {
        var tiles = new List<Tile>();
        if (ctx?.ability == null || ctx.caster == null) return tiles;

        var start = ctx.caster.currentTile;
        if (start == null) return tiles;

        if (ctx.targetTile != null && IsWithinRange(ctx, ctx.targetTile))
            tiles.Add(ctx.targetTile);

        return tiles;
    }

    public bool IsWithinRange(AbilityContext ctx, Tile targetTile)
    {
        if (ctx?.ability == null || ctx.caster == null || targetTile == null) return false;

        var start = ctx.caster.currentTile;
        if (start == null) return false;
        if (start == targetTile) return false;

        int distance = GridManager.Instance.GetGridDistance(start, targetTile);
        if (distance > ctx.EffectiveRange || distance <= 0) return false;
        if (!affectsOverGaps && !targetTile.passableTerrain) return false;
        if (!affectsThroughWalls && IsBlockedByWall(start, targetTile)) return false;

        return true;
    }
}