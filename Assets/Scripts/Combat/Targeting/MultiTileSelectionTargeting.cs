using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Targeting mode where the player clicks individual tiles one at a time.
    /// The ability fires automatically once the required number of unique valid tiles
    /// have been selected — no separate confirm step needed.
    ///
    /// CombatManager drives the session:
    ///   BeginSelection()   — called when the player activates the ability
    ///   TrySelectTile()    — called on each tile click; returns true if the tile was accepted
    ///   CancelSelection()  — called on Escape or ability deselect
    ///   IsComplete         — true once selectionCount unique tiles have been chosen
    ///
    /// GetTraversal() returns the selected tiles so AbilityEffect subclasses (e.g.
    /// ApplyTileEffectAbilityEffect with OnTraversalTiles mode) receive them as targets.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Targeting > Multi-Tile Selection
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Multi-Tile Selection")]
    public class MultiTileSelectionTargeting : AbilityTargeting
    {
        [Header("Selection Settings")]
        [Tooltip("How many tiles the player must choose before the ability fires.")]
        [SerializeField] private int selectionCount = 4;

        [Tooltip("If true, tiles occupied by a unit can be selected. " +
                 "If false, the player can only pick empty tiles.")]
        [SerializeField] public bool allowOccupiedTiles = true;

        // ── Runtime selection state ───────────────────────────────────────────────
        // Stored on the ScriptableObject for the duration of one selection session.
        // BeginSelection resets it; CancelSelection clears it.
        private readonly List<Tile> _selectedTiles = new List<Tile>();
        private bool _selectionActive = false;

        public int SelectionCount => selectionCount;
        
        /// <summary>Read-only view of the tiles chosen so far.</summary>
        public IReadOnlyList<Tile> SelectedTiles => _selectedTiles;

        /// <summary>True once the required number of unique tiles have been selected.</summary>
        public bool IsComplete => _selectedTiles.Count >= selectionCount;
        

        // ── Session control ───────────────────────────────────────────────────────

        /// <summary>
        /// Starts a new selection session. Called by CombatManager.EnterAbilityTargeting.
        /// </summary>
        public void BeginSelection(AbilityContext ctx)
        {
            _selectedTiles.Clear();
            _selectionActive = true;
        }

        /// <summary>
        /// Cancels the current session and clears all selections.
        /// Called by CombatManager.CancelAbilityTargeting.
        /// </summary>
        public void CancelSelection()
        {
            _selectedTiles.Clear();
            _selectionActive = false;
        }

        /// <summary>
        /// Attempts to add a tile to the current selection.
        /// Returns true if the tile was accepted (valid, not already chosen, session active).
        /// Returns false if the tile was rejected — the caller should stay in targeting mode.
        /// </summary>
        public bool TrySelectTile(Tile tile, AbilityContext ctx)
        {
            if (!_selectionActive) return false;
            if (tile == null) return false;
            if (_selectedTiles.Contains(tile)) return false;  // Must be unique
            if (!IsValidSelection(tile, ctx)) return false;

            _selectedTiles.Add(tile);
            return true;
        }

        /// <summary>
        /// Returns true if a tile is a valid pick: within range, passable, and passes the
        /// allowOccupiedTiles check. Used by both TrySelectTile and the hover preview.
        /// </summary>
        public bool IsValidSelection(Tile tile, AbilityContext ctx)
        {
            if (tile == null || !tile.passableTerrain) return false;
            if (!allowOccupiedTiles && tile.occupied) return false;

            // Range check reads from ctx.EffectiveRange, consistent with SingleTargeting.
            // affectsOverGaps and affectsThroughWalls are inherited from AbilityTargeting,
            // so wall and gap rules also behave identically to other targeting types.
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

        /// <summary>
        /// Returns all tiles within range that satisfy the selection rules.
        /// Used by CombatManager.ShowMultiTileSelectionPreview to highlight the selectable area.
        /// </summary>
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

        // ── AbilityTargeting overrides ────────────────────────────────────────────

        /// <summary>
        /// Returns the selected tiles as the traversal list so AbilityEffect subclasses
        /// (e.g. ApplyTileEffectAbilityEffect with OnTraversalTiles) receive them correctly.
        /// </summary>
        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            // Return a copy so external code cannot mutate the selection list.
            return new List<Tile>(_selectedTiles);
        }

        /// <summary>
        /// Selected tiles with units on them become targets. Respects canHitAllies / canHitEnemies.
        /// </summary>
        public override List<Unit> SelectTargets(AbilityContext ctx)
        {
            var result = new List<Unit>();
            if (ctx?.caster == null) return result;

            foreach (var tile in _selectedTiles)
            {
                var unit = tile.currentUnit;
                if (unit == null) continue;

                bool isAlly = IsAlly(ctx.caster, unit);
                if ((isAlly && ctx.ability.canHitAllies) || (!isAlly && ctx.ability.canHitEnemies))
                    result.Add(unit);
            }
            return result;
        }
    }
}