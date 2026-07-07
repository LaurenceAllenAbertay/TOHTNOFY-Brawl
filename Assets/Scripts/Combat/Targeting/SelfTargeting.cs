using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Self")]
    public class SelfTargeting : AbilityTargeting
    {
        public override bool UsesDirectionalInput => false;
        public override bool UsesHoverTracking    => false;
        public override bool ConfirmsOnTileClick  => true;
        
        public override void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile)
        {
            GridManager.Instance.ClearAllHighlights();

            ctx.caster?.currentTile?.Highlight(TileHighlightType.AttackRange);
        }

        public override bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
        {
            outCtx = new AbilityContext
            {
                caster     = ctx.caster,
                ability    = ctx.ability,
                targetTile = ctx.caster?.currentTile,
                aimDir     = Vector2Int.zero
            };
            return true;
        }
        
        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            var tiles = new List<Tile>();
            if (ctx?.caster?.currentTile != null)
                tiles.Add(ctx.caster.currentTile);
            return tiles;
        }
        
        public override List<Unit> SelectTargets(AbilityContext ctx)
        {
            var result = new List<Unit>();
            if (ctx?.caster != null)
                result.Add(ctx.caster);
            return result;
        }
    }
}