using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Effects/Damage Effect")]
public class DamageEffect : AbilityEffect
{
    public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
    {
        if (ctx == null || ctx.ability == null || targets == null) return;
        int dmg = ctx.ability.damage;

        foreach (var u in targets)
        {
            if (u == null) continue;
            u.ReceiveDamage(dmg);
        }
    }
}