using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Damage Effect")]
    public class DamageEffect : AbilityEffect
    {
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Damage;
        public override string TargetAnimationHint => "Hurt";
        public override float ExpectedAnimationDuration => 0.8f;

        private const int DamageVarianceRange = 4;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx == null || ctx.ability == null || targets == null) return;

            int rawAttack = ctx.caster != null ? ctx.caster.baseAttack : 0;
            float attackMultiplier = ctx.caster != null ? StatusEffectManager.GetAttackMultiplier(ctx.caster) : 1f;
            int baseDamage = Mathf.RoundToInt((ctx.ability.damage + rawAttack) * attackMultiplier);

            foreach (var u in targets)
            {
                if (u == null) continue;
                int variance = Random.Range(-DamageVarianceRange, DamageVarianceRange + 1);
                int dmg = Mathf.Max(1, baseDamage + variance - u.currentDefense);
                u.ReceiveDamage(dmg, ctx.caster, ctx.ability);
                UnitManager.NotifyUnitDamaged(u, ctx.caster);
                
                ctx.LastResolvedDamage += dmg;
            }
        }
    }
}