using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Recoil Damage Effect")]
    public class RecoilDamageEffect : AbilityEffect
    {
        [Range(0f, 1f)]
        public float recoilFraction = 0.25f;

        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;
        
        public override string SelfCastAnimationHint => "Debuff";

        public override bool IsSelfOnly(AbilityContext ctx) => true;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;

            int resolvedDamage = ctx.LastResolvedDamage;
            if (resolvedDamage <= 0) return;

            int recoil = Mathf.Max(1, Mathf.RoundToInt(resolvedDamage * recoilFraction));
            
            ctx.caster.currentHealth -= recoil;
            Debug.Log($"[RecoilDamageEffect] {ctx.caster.name} took {recoil} recoil " +
                      $"({recoilFraction * 100}% of {resolvedDamage} damage dealt).");
            
            Unit.NotifyHealthChanged(ctx.caster);
            
            Unit.NotifyDamageDealt(ctx.caster, recoil);

            if (ctx.caster.currentHealth <= 0)
            {
                Debug.Log($"[RecoilDamageEffect] {ctx.caster.name} was defeated by recoil.");
                ctx.caster.Die(killer: null); 
            }
        }
    }
}