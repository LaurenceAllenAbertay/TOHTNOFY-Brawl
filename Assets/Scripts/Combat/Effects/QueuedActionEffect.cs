using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Queued Action Effect")]
    public class QueuedActionEffect : AbilityEffect
    {
        public Ability followUpAbility;

        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null || followUpAbility == null) return;

            ctx.caster.pendingAction = new PendingAction(followUpAbility, ctx.aimDir);

            Debug.Log($"[QueuedAction] {ctx.caster.name} queued {followUpAbility.abilityName} " +
                      $"for next turn (dir: {ctx.aimDir})");
        }
    }
}