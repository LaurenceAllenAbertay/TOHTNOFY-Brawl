using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Effects/Damage Effect")]
public class DamageEffect : AbilityEffect
{
    public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        if (ctx == null || ctx.ability == null || targets == null) return;

        // Base damage is the ability's flat damage value plus the caster's current attack stat.
        // Each target's currentDefense is then subtracted, clamped to a minimum of 1
        // so damage abilities always deal at least 1 damage.
        int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);

        foreach (var u in targets)
        {
            if (u == null) continue;
            int dmg = Mathf.Max(1, baseDamage - u.currentDefense);
            u.ReceiveDamage(dmg);
        }
    }
}