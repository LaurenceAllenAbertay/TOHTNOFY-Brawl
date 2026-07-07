using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Apply Tile Effect")]
    public class ApplyTileEffectAbilityEffect : AbilityEffect
    {
        [Header("Tile Effect to Place")]
        [SerializeField] private TileEffectData tileEffectData;

        [SerializeField] private int rounds = 2;

        [SerializeField] private float effectPower = 10f;

        [Header("Placement Mode")]
        [SerializeField] private PlacementMode placementMode = PlacementMode.OnTargetUnits;
        
        [SerializeField] private bool triggerImmediatelyIfOccupied = false;

        public enum PlacementMode
        {
            OnTargetUnits,
            OnTraversalTiles
        }

        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;

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
            
            if (triggerImmediatelyIfOccupied &&
                tileEffectData.triggerTiming == TriggerTiming.OnEnter &&
                tile.currentUnit != null)
            {
                tile.StartCoroutine(tile.TriggerOnEnterEffects(tile.currentUnit));
            }
        }
    }
}