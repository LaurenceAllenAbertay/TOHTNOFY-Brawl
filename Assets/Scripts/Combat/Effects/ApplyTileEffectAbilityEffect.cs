using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// AbilityEffect subclass that places a TileEffect onto tiles when an ability fires.
    /// This closes the data loop: an ability can now mark ground as hazardous the same way
    /// it applies damage or status effects — just add this to the ability's effects list.
    ///
    /// Two placement modes:
    ///   OnTargetUnits    — places the effect on the tile each hit unit is currently standing on.
    ///                      Use for: fire DoT on whoever you struck, poison a unit's position.
    ///   OnTraversalTiles — places the effect on every tile the ability traverses, including empty
    ///                      ones. Use for: fire line that burns the ground, AOE that leaves acid.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Effects > Apply Tile Effect
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Apply Tile Effect")]
    public class ApplyTileEffectAbilityEffect : AbilityEffect
    {
        [Header("Tile Effect to Place")]
        [Tooltip("The TileEffectData asset defining what the hazard does each round.")]
        [SerializeField] private TileEffectData tileEffectData;

        [Tooltip("How many rounds the tile effect persists before expiring.")]
        [SerializeField] private int rounds = 2;

        [Tooltip("Power passed into TileEffectContext.effectPower (damage per round, heal per round, etc.).")]
        [SerializeField] private float effectPower = 10f;

        [Header("Placement Mode")]
        [Tooltip("OnTargetUnits:    place the effect on each hit unit's current tile.\n" +
                 "OnTraversalTiles: place the effect on every tile the ability path crosses, " +
                 "including empty ones.")]
        [SerializeField] private PlacementMode placementMode = PlacementMode.OnTargetUnits;

        [Tooltip("If true and the tile is already occupied when the effect is placed, " +
                 "TriggerOnEnterEffects fires immediately on that unit. " +
                 "Use for OnEnter effects where a unit standing on the tile at cast time " +
                 "should be hit straight away rather than waiting for their next move.")]
        [SerializeField] private bool triggerImmediatelyIfOccupied = false;

        public enum PlacementMode
        {
            /// <summary>Places the tile effect on the tile each hit unit is currently standing on.</summary>
            OnTargetUnits,

            /// <summary>
            /// Places the tile effect on every tile the ability traverses (via targeting.GetTraversal),
            /// regardless of whether a unit is present. Use for ground-marking abilities.
            /// </summary>
            OnTraversalTiles
        }

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (tileEffectData == null)
            {
                Debug.LogWarning("[ApplyTileEffectAbilityEffect] No TileEffectData assigned!");
                return;
            }

            switch (placementMode)
            {
                case PlacementMode.OnTargetUnits:
                    PlaceOnTargetTiles(ctx, targets);
                    break;

                case PlacementMode.OnTraversalTiles:
                    PlaceOnTraversalTiles(ctx);
                    break;
            }
        }

        // ── Placement helpers ─────────────────────────────────────────────────────

        private void PlaceOnTargetTiles(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            foreach (var target in targets)
            {
                if (target?.currentTile == null) continue;
                PlaceEffect(target.currentTile, ctx.caster);
            }
        }

        private void PlaceOnTraversalTiles(AbilityContext ctx)
        {
            if (ctx?.ability?.targeting == null) return;

            var traversalTiles = ctx.ability.targeting.GetTraversal(ctx);
            foreach (var tile in traversalTiles)
            {
                if (tile == null) continue;
                PlaceEffect(tile, ctx.caster);
            }
        }

        private void PlaceEffect(Tile tile, Unit applier)
        {
            tile.AddEffect(new TileEffectInstance(tileEffectData, applier, rounds, effectPower));

            // If the tile is already occupied and immediate triggering is enabled,
            // fire OnEnter effects on the occupant now. This handles the case where a
            // unit is standing on a tile when the hazard is placed — they shouldn't be
            // immune just because they didn't "walk through" it.
            if (triggerImmediatelyIfOccupied &&
                tileEffectData.triggerTiming == TriggerTiming.OnEnter &&
                tile.currentUnit != null)
            {
                tile.StartCoroutine(tile.TriggerOnEnterEffects(tile.currentUnit));
            }
        }
    }
}