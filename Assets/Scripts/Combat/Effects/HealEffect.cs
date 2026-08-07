using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Heal Effect")]
    public class HealEffect : AbilityEffect
    {
        [Header("Heal Amount")]
        [SerializeField] private int flatHeal = 10;

        [SerializeField] [Range(0f, 2f)] private float attackScaling = 0f;

        private const int HealVarianceRange = 4;
        
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.StatusBuff;

        public override string TargetAnimationHint => "Buff";
        
        public override float ExpectedAnimationDuration => 0.6f;
        
        public override bool IsBuffEffect => true;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (targets == null) return;

            int healAmount = flatHeal;
            if (attackScaling > 0f && ctx?.caster != null)
                healAmount += Mathf.RoundToInt(ctx.caster.currentAttack * attackScaling);

            if (healAmount <= 0) return;

            foreach (var target in targets)
            {
                if (target == null || target.IsDead) continue;
                
                if (target.IsBody) continue;

                int variance = Random.Range(-HealVarianceRange, HealVarianceRange + 1);
                int variedHeal = Mathf.Max(0, healAmount + variance);

                variedHeal = Mathf.RoundToInt(variedHeal * StatusEffectManager.GetHealingMultiplier(target));

                if (variedHeal <= 0) continue;

                int maxHealth = target.maxHealth > 0
                    ? target.maxHealth
                    : target.currentHealth;

                int before = target.currentHealth;
                target.currentHealth = Mathf.Min(target.currentHealth + variedHeal, maxHealth);
                int actualHeal = target.currentHealth - before;

                if (actualHeal <= 0) continue;
                
                Unit.NotifyHealthChanged(target);

                Debug.Log($"[HealEffect] {target.name} restored {actualHeal} HP " +
                          $"({before} → {target.currentHealth} / {maxHealth}).");
            }
        }
    }
}