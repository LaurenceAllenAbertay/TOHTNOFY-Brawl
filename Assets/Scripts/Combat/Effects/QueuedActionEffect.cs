using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Ability effect that queues a follow-up ability to auto-execute on the caster's next turn.
    /// The aim direction from the current cast is preserved so directional abilities
    /// (e.g. Chug Explosion) fire in the direction the player chose.
    ///
    /// Place this on any ability that should charge up and fire next turn.
    /// The ability that owns this effect should also have endTurnOnCast = true.
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Queued Action Effect")]
    public class QueuedActionEffect : AbilityEffect
    {
        [Tooltip("The ability that will auto-execute on the caster's next turn.")]
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