using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Teleport Effect")]
    public class TeleportEffect : AbilityEffect
    {
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;
        public override float ExpectedAnimationDuration => 0.5f;
        public override bool RequiresEmptyTargetTile => true;

        public override bool NeedsCameraPreview(AbilityContext ctx, out Tile focusTile) =>
            TryGetValidDestination(ctx, out focusTile);

        public bool TryGetValidDestination(AbilityContext ctx, out Tile destinationTile)
        {
            destinationTile = null;
            if (ctx?.caster?.currentTile == null || ctx.ability?.targeting == null) return false;

            var targetTiles = ctx.ability.targeting.GetTraversal(ctx);
            if (targetTiles == null || targetTiles.Count == 0) return false;

            var candidateTile = targetTiles[0];
            if (candidateTile == null || candidateTile.occupied || !candidateTile.passableTerrain) return false;

            destinationTile = candidateTile;
            return true;
        }

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (TryGetValidDestination(ctx, out var destinationTile))
            {
                ctx.caster.SetCurrentTile(destinationTile);
                Debug.Log($"{ctx.caster.name} teleported to {destinationTile.name}");
                return;
            }

            Debug.Log("Teleport failed - no valid destination found");
        }
    }
}