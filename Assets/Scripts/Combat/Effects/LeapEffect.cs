using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Leap Effect")]
    public class LeapEffect : AbilityEffect
    {
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;
        public override float ExpectedAnimationDuration => 1f;

        public override bool NeedsCameraPreview(AbilityContext ctx, out Tile focusTile)
        {
            focusTile = ctx?.targetTile;
            return focusTile != null;
        }

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;

            var destination = ctx.targetTile;
            if (destination == null)
            {
                Debug.Log("[Leap] No destination tile in the AbilityContext — leap aborted.");
                return;
            }

            var jumpSystem = Object.FindAnyObjectByType<JumpSystem>();
            if (jumpSystem == null)
            {
                Debug.LogWarning("[Leap] No JumpSystem in the scene — leap aborted.");
                return;
            }

            var occupant = destination.currentUnit;

            if (occupant != null && (targets == null || !targets.Contains(occupant)))
            {
                Debug.Log($"[Leap] {occupant.name} is not a legal target for {ctx.ability.abilityName} " +
                          "and the tile cannot be shared — leap aborted.");
                return;
            }

            ctx.caster.StartCoroutine(RunLeap(ctx, jumpSystem, destination, occupant));
        }

        private IEnumerator RunLeap(AbilityContext ctx, JumpSystem jumpSystem, Tile destination, Unit stompTarget)
        {
            var caster = ctx.caster;
            Tile originTile = caster.currentTile;

            Vector3 startPos = caster.transform.position;
            Vector3 endPos   = destination.transform.position;

            caster.SetCurrentTileLogical(destination);

            yield return caster.StartCoroutine(jumpSystem.JumpAnimation(
                caster, startPos, endPos, stompTarget, originTile, ctx.ability.damage));

            UIEvents.OnUnitMoved();
        }
    }
}