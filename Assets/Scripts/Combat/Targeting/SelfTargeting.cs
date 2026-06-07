using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Targeting type for abilities that affect only the caster (e.g. Bunker Down).
    ///
    /// Player flow:
    ///   1. Player presses the ability button → EnterAbilityTargeting → ShowEnterPreview
    ///      highlights the caster's tile so it is clear this is a self-cast.
    ///   2. Player clicks anywhere (they will naturally click their own highlighted tile)
    ///      → OnTileClicked confirms immediately — no range check needed.
    ///   3. AbilityTargetingController calls StartAbilityExecution with the caster context.
    ///
    /// The StatusEffect's IsSelfOnly / SelfCastAnimationHint then route the effect through
    /// the caster-buff path in AbilitySequencer without a per-target pass.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Targeting > Self
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Targeting/Self")]
    public class SelfTargeting : AbilityTargeting
    {
        // ── Input behaviour ───────────────────────────────────────────────────────
        // No directional input, no hover tracking.
        // Confirms on the next tile click — the player clicks their highlighted tile.

        public override bool UsesDirectionalInput => false;
        public override bool UsesHoverTracking    => false;
        public override bool ConfirmsOnTileClick  => true;

        // ── Preview ───────────────────────────────────────────────────────────────

        public override void ShowEnterPreview(AbilityContext ctx, Tile hoveredTile)
        {
            GridManager.Instance.ClearAllHighlights();

            // Highlight the caster's tile so it is clear this is a self-cast ability.
            ctx.caster?.currentTile?.Highlight(TileHighlightType.AttackRange);
        }

        // ── Confirmation ──────────────────────────────────────────────────────────

        /// <summary>
        /// Confirms on any tile click — the player has already been shown the caster
        /// tile highlight, so any click is a deliberate confirmation.
        /// outCtx is set to a fresh context carrying the caster so StartAbilityExecution
        /// has everything it needs.
        /// </summary>
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

        // ── Traversal ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the caster's tile as the sole traversal tile.
        /// Required by the abstract base; SelectTargets below overrides target resolution.
        /// </summary>
        public override List<Tile> GetTraversal(AbilityContext ctx)
        {
            var tiles = new List<Tile>();
            if (ctx?.caster?.currentTile != null)
                tiles.Add(ctx.caster.currentTile);
            return tiles;
        }

        // ── Target selection ──────────────────────────────────────────────────────

        /// <summary>
        /// Always returns the caster as the sole target.
        /// AbilitySequencer's HandleRemainingEffects checks SelfCastAnimationHint on each
        /// effect: for a Caster-only StatusEffect this fires the caster-buff animation
        /// path rather than a per-target pass, so no external targets are needed.
        /// </summary>
        public override List<Unit> SelectTargets(AbilityContext ctx)
        {
            var result = new List<Unit>();
            if (ctx?.caster != null)
                result.Add(ctx.caster);
            return result;
        }
    }
}