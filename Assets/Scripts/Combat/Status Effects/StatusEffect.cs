using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Status Effect")]
    public class StatusEffect : AbilityEffect
    {
        public enum ApplicationTarget
        {
            Targets, 
            Caster,  
            Both       
        }

        [System.Serializable]
        public class StatusApplication
        {
            [Header("Basic Settings")]
            public StatusEffectData statusEffectData;
            public ApplicationTarget applyTo = ApplicationTarget.Targets;
            public int duration = 2;
            
            public int allyDuration = 0;

            [Header("Effect Power")]
            public float effectPower = 0;

            [Header("Scaling Options")]
            public bool useAbilityDamage = false;
            
            public float damageMultiplier = 1f;

            public bool scaleDurationWithDamage = false;
            public float damagePerTurn = 10f; 

            [Header("Warned Fallback")]
            public StatusEffectData warnedFallbackEffect;
        }

        public List<StatusApplication> statusesToApply = new List<StatusApplication>();
        
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.StatusBuff;
        public override float ExpectedAnimationDuration => 0.6f;


        public override bool IsBuffEffect =>
            statusesToApply.Any(sa => sa.statusEffectData != null &&
                                      sa.statusEffectData.effectType.IsBuffType());
        
        public override string SelfCastAnimationHint
        {
            get
            {
                var selfApps = statusesToApply.Where(sa =>
                    sa.applyTo == ApplicationTarget.Caster ||
                    sa.applyTo == ApplicationTarget.Both).ToList();
                if (selfApps.Count == 0) return null;
                bool isBuff = selfApps.Any(sa => sa.statusEffectData != null &&
                                                 sa.statusEffectData.effectType.IsBuffType());
                return isBuff ? "Buff" : "Debuff";
            }
        }
        
        public override bool IsSelfOnly(AbilityContext ctx) =>
            statusesToApply.Count > 0 &&
            statusesToApply.All(sa => sa.applyTo == ApplicationTarget.Caster);

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (StatusEffectManager.Instance == null)
            {
                Debug.LogError("StatusEffectManager not found!");
                return;
            }

            foreach (var status in statusesToApply)
            {
                if (status.statusEffectData == null) continue;
                
                int finalDuration = status.duration;
                float finalPower = status.effectPower;
                
                if (ctx.ability != null)
                {
                    if (status.useAbilityDamage)
                    {
                        finalPower = ctx.ability.damage * status.damageMultiplier;
                    }

                    if (status.scaleDurationWithDamage && status.damagePerTurn > 0)
                    {
                        finalDuration = Mathf.Max(1, Mathf.RoundToInt(ctx.ability.damage / status.damagePerTurn));
                    }
                }
                
                if (status.applyTo == ApplicationTarget.Caster || status.applyTo == ApplicationTarget.Both)
                {
                    ApplyToUnit(ctx.caster, status, ctx.caster, finalDuration, finalPower);
                }
                
                if (status.applyTo == ApplicationTarget.Targets || status.applyTo == ApplicationTarget.Both)
                {
                    foreach (var target in targets)
                    {
                        if (target == null) continue;
                        
                        if (status.applyTo == ApplicationTarget.Both && target == ctx.caster) continue;
                        
                        bool isAlly = target == ctx.caster || target.IsAllyOf(ctx.caster);
                        int durationForTarget = (isAlly && status.allyDuration != 0)
                            ? status.allyDuration
                            : finalDuration;

                        ApplyToUnit(target, status, ctx.caster, durationForTarget, finalPower);
                    }
                }
            }
        }

        private void ApplyToUnit(Unit target, StatusApplication status, Unit source, int duration, float power)
        {
            var instance = StatusEffectManager.Instance.ApplyStatusEffect(
                target,
                status.statusEffectData,
                source,
                duration,
                power
            );
            
            if (instance != null &&
                status.statusEffectData.effectType == StatusEffectType.Warned &&
                status.warnedFallbackEffect != null)
            {
                instance.customData = status.warnedFallbackEffect;
            }
            
            string effectDescription = GetEffectDescription(status.statusEffectData, power, duration);
            Debug.Log($"{target.name} {effectDescription}");
        }

        private string GetEffectDescription(StatusEffectData effectData, float power, int duration)
        {
            switch (effectData.effectType)
            {
                case StatusEffectType.AttackUp:
                    return $"gains +{power} Attack for {duration} turns";
                case StatusEffectType.AttackDown:
                    return $"loses {power} Attack for {duration} turns";
                case StatusEffectType.DefenseUp:
                    return $"gains +{power} Defense for {duration} turns";
                case StatusEffectType.DefenseDown:
                    return $"loses {power} Defense for {duration} turns";
                case StatusEffectType.SpeedUp:
                    return $"gains +{power} Speed for {duration} turns";
                case StatusEffectType.SpeedDown:
                    return $"loses {power} Speed for {duration} turns";
                case StatusEffectType.Bleed:
                    return $"is bleeding ({power} damage/turn, {duration} stacks)";
                case StatusEffectType.Poison:
                    return $"is poisoned for {duration} turns";
                case StatusEffectType.Shielded:
                    return $"gains a shield for {duration} turns";
                case StatusEffectType.Guarded:
                    return $"gains {power} HP guard for {duration} turns";
                default:
                    return $"is affected by {effectData.effectName} for {duration} turns";
            }
        }
    }
}