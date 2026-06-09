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
            Targets,    // Apply to ability targets (default)
            Caster,     // Apply to caster only
            Both        // Apply to both caster and targets
        }

        [System.Serializable]
        public class StatusApplication
        {
            [Header("Basic Settings")]
            public StatusEffectData statusEffectData;
            public ApplicationTarget applyTo = ApplicationTarget.Targets;
            public int duration = 2;

            [Tooltip("If non-zero, allies receive this duration instead of 'duration'. " +
                     "Useful for abilities like Bass Riff that debuff allies briefly and enemies longer. " +
                     "Leave at 0 to use 'duration' for everyone.")]
            public int allyDuration = 0;

            [Header("Effect Power")]
            [Tooltip("Generic power value - damage for DoTs, stat change amount for buffs, shield HP for guards, etc.")]
            public float effectPower = 0;

            [Header("Scaling Options")]
            [Tooltip("If true, effectPower equals ability damage")]
            public bool useAbilityDamage = false;

            [Tooltip("Multiplier applied to ability damage if useAbilityDamage is true")]
            public float damageMultiplier = 1f;

            [Tooltip("If true, duration scales with ability damage (1 turn per X damage)")]
            public bool scaleDurationWithDamage = false;
            public float damagePerTurn = 10f; // If ability does 30 damage and this is 10, duration = 3

            [Header("Warned Fallback")]
            [Tooltip("Only used when statusEffectData is Warned. " +
                     "If the Warned unit never dodges before the effect expires, " +
                     "this StatusEffectData is applied instead (typically DefenseDown).")]
            public StatusEffectData warnedFallbackEffect;
        }

        public List<StatusApplication> statusesToApply = new List<StatusApplication>();

        // The sequencer further sub-sorts within the Status phase (buff vs debuff) using IsBuffEffect.
        // Phase here is StatusBuff as a sensible default; debuff-only StatusEffects will still be
        // sorted correctly because PlayTargetEffectsWithAnimation partitions all status effects by
        // IsBuffEffect regardless of this property.
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.StatusBuff;
        public override float ExpectedAnimationDuration => 0.6f;

        /// <summary>
        /// True when at least one status in this effect is a buff type.
        /// Used by AbilitySequencer to choose Buff vs Debuff target animation.
        /// </summary>
        public override bool IsBuffEffect =>
            statusesToApply.Any(sa => sa.statusEffectData != null &&
                                      sa.statusEffectData.effectType.IsBuffType());

        /// <summary>
        /// Animation hint for the caster when self-application statuses are present.
        /// Buff if any self-applied status is a buff type, Debuff otherwise.
        /// </summary>
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

        /// <summary>
        /// Returns true when every application in this status effect targets only the caster,
        /// so the sequencer knows to skip it during the per-target pass.
        /// </summary>
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

                // Calculate final values
                int finalDuration = status.duration;
                float finalPower = status.effectPower;

                // Handle scaling with ability damage
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

                // Apply to caster if needed
                if (status.applyTo == ApplicationTarget.Caster || status.applyTo == ApplicationTarget.Both)
                {
                    ApplyToUnit(ctx.caster, status, ctx.caster, finalDuration, finalPower);
                }

                // Apply to targets if needed
                if (status.applyTo == ApplicationTarget.Targets || status.applyTo == ApplicationTarget.Both)
                {
                    foreach (var target in targets)
                    {
                        if (target == null) continue;

                        // When applyTo is Both, the caster was already handled by the block above.
                        // Skip them here to prevent a double application when the caster falls
                        // within their own AOE (e.g. Toughen Up buffing allies within range 2).
                        if (status.applyTo == ApplicationTarget.Both && target == ctx.caster) continue;

                        // Use allyDuration for allies when it has been explicitly set (non-zero).
                        // Falls back to finalDuration for enemies and when allyDuration is unset.
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

            // For Warned effects, store the fallback StatusEffectData in customData so
            // StatusEffectManager can apply it at expiry if the dodge never triggered.
            if (instance != null &&
                status.statusEffectData.effectType == StatusEffectType.Warned &&
                status.warnedFallbackEffect != null)
            {
                instance.customData = status.warnedFallbackEffect;
            }

            // Log based on effect type for clarity
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
                case StatusEffectType.Bleeding:
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