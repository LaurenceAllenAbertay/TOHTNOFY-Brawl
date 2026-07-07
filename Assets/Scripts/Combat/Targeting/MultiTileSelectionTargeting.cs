using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Multi-Tile Selection")]
    public class MultiTileSelectionTargeting : AbilityTargeting
    {
        [Header("Selection Settings")]
        [SerializeField] private int selectionCount = 4;
        
        [SerializeField] public bool allowOccupiedTiles = true;
        
        private readonly List<Tile> _selectedTiles = new List<Tile>();
        private bool _selectionActive = false;

        public int SelectionCount => selectionCount;
        
        public IReadOnlyList<Tile> SelectedTiles => _selectedTiles;
        
        public bool IsComplete => _selectedTiles.Count >= selectionCount;
        
        public void BeginSelection(AbilityContext ctx)
        {
            _selectedTiles.Clear();
            _selectionActive = true;
        }
        
        public void CancelSelection()
        {
            _selectedTiles.Clear();
            _selectionActive = false;
        }
        
        public bool TrySelectTile(Tile tile, AbilityContext ctx)
        {
            if (!_selectionActive) return false;
            if (tile == null) return false;
            if (_selectedTiles.Contains(tile)) return false;  
            if (!IsValidSelection(tile, ctx)) return false;

            _selectedTiles.Add(tile);
            return true;
        }
        
        public bool IsValidSelection(Tile tile, AbilityContext ctx)
        {
            if (tile == null || !tile.passableTerrain) return false;
            if (!allowOccupiedTiles && tile.occupied) return false;
            
            if (ctx?.caster?.currentTile != null && ctx.ability != null)
            {
                int dist = GridManager.Instance.GetGridDistance(ctx.caster.currentTile, tile);
                if (dist > ctx.EffectiveRange || dist <= 0) return false;

                if (!affectsOverGaps && !tile.passableTerrain) return false;

                if (!affectsThroughWalls && IsBlockedByWall(ctx.caster.currentTile, tile))
                    return false;
            }

            return true;
        }
        
        public List<Tile> GetTilesInRange(AbilityContext ctx)
        {
            var result = new List<Tile>();
            if (GridManager.Instance == null) return result;

            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (IsValidSelection(tile, ctx))
                    result.Add(tile);
            }
            return result;
        }

        public override bool UsesDirectionalInput => false;
        public override bool ConfirmsOnTileClick  => true;

        public override void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile)
        {
            BeginSelection(ctx);
            DrawSelectionHighlights(ctx, hoveredTile);
        }

        public override void ShowHoverPreview(AbilityContext ctx, Tile hoveredTile)
        {
            DrawSelectionHighlights(ctx, hoveredTile);
        }

        public override void OnMouseMoved(AbilityContext ctx, Tile hoveredTile)
        {
            DrawSelectionHighlights(ctx, hoveredTile);
        }

        public override bool OnTileClicked(Tile tile, AbilityContext ctx, out AbilityContext outCtx)
        {
            outCtx = null;
            if (!TrySelectTile(tile, ctx)) return false;

            if (IsComplete)
            {
                outCtx = new AbilityContext
                {
                    caster  = ctx.caster,
                    ability = ctx.ability
                };
                return true;
            }
            
            DrawSelectionHighlights(ctx, null);
            return false;
        }

        public override void OnCancel(AbilityContext ctx)
        {
            CancelSelection();
        }

        private void DrawSelectionHighlights(AbilityContext ctx, Tile hoveredTile)
        {
            GridManager.Instance.ClearAllHighlights();

            foreach (var tile in GetTilesInRange(ctx))
            {
                if (!_selectedTiles.Contains(tile))
                    tile.Highlight(TileHighlightType.Moveable);
            }

            foreach (var tile in _selectedTiles)
                tile.Highlight(TileHighlightType.AttackRange);

            if (hoveredTile != null && IsValidSelection(hoveredTile, ctx))
                hoveredTile.Highlight(TileHighlightType.Occupied);
        }
        
        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            return new List<Tile>(_selectedTiles);
        }
        
        public override List<Unit> SelectTargets(AbilityContext ctx)
        {
            var result = new List<Unit>();
            if (ctx?.caster == null) return result;

            foreach (var tile in _selectedTiles)
            {
                var unit = tile.currentUnit;
                if (unit == null) continue;

                bool accepted = false;

                if (unit.IsNeutral)
                {
                    if (ctx.ability.canTargetNeutral)
                        accepted = true;
                }
                else
                {
                    bool isAlly = IsAlly(ctx.caster, unit);
                    if ((isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies))
                        accepted = true;
                }

                if (accepted)
                    result.Add(unit);
            }
            return result;
        }
    }
}