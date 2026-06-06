using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Mastermind Passive (Lorns)
    ///
    /// Part 1 - Potency boost: any status effect Lorns applies (to anyone) has its
    /// effectPower increased by a flat amount before the instance is created.
    /// Hooks into StatusEffectManager.OnModifyEffectPower.
    ///
    /// Part 2 - Mirror buff: when any ally within 5 tiles receives AttackUp, DefenseUp,
    /// or SpeedUp (from any source), Lorns also receives the same buff type at effectPower 1
    /// for the same duration. Does not trigger when Lorns himself receives a buff (avoids
    /// reflecting his own mirror back onto himself).
    /// Hooks into StatusEffectManager.OnStatusEffectApplied.
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Mastermind")]
    public class MastermindPassive : PassiveAbility
    {
        [Header("Potency Boost")]
        [Tooltip("Flat amount added to effectPower on every status effect Lorns applies.")]
        public float potencyBonus = 1f;

        [Header("Mirror Buff")]
        [Tooltip("Range within which an ally must be for Lorns to mirror their buff.")]
        public int mirrorRange = 5;

        [Header("Mirror Buff Status Effect Data")]
        [Tooltip("AttackUp StatusEffectData asset.")]
        public StatusEffectData attackUpData;
        [Tooltip("DefenseUp StatusEffectData asset.")]
        public StatusEffectData defenseUpData;
        [Tooltip("SpeedUp StatusEffectData asset.")]
        public StatusEffectData speedUpData;

        // Stored delegates for clean unsubscription.
        private System.Func<Unit, Unit, StatusEffectData, float, float> powerModifierHandler;
        private System.Action<Unit, StatusEffectInstance> statusAppliedHandler;

        // Cached so inner lambdas can reference it without capturing a possibly-stale handler.
        private PassiveAbilityHandler cachedHandler;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            cachedHandler = handler;

            // Part 1: register potency boost — only fires when Lorns is the source.
            powerModifierHandler = (source, target, effectData, power) =>
            {
                if (source == cachedHandler.Owner)
                    return power + potencyBonus;
                return power;
            };
            StatusEffectManager.OnModifyEffectPower += powerModifierHandler;

            // Part 2: register mirror buff — fires whenever any status effect is applied.
            statusAppliedHandler = (target, effect) => TryMirrorBuff(target, effect);
            StatusEffectManager.OnStatusEffectApplied += statusAppliedHandler;
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            if (powerModifierHandler != null)
            {
                StatusEffectManager.OnModifyEffectPower -= powerModifierHandler;
                powerModifierHandler = null;
            }

            if (statusAppliedHandler != null)
            {
                StatusEffectManager.OnStatusEffectApplied -= statusAppliedHandler;
                statusAppliedHandler = null;
            }

            cachedHandler = null;
        }

        private void TryMirrorBuff(Unit target, StatusEffectInstance effect)
        {
            if (cachedHandler == null || cachedHandler.Owner == null) return;

            Unit lorns = cachedHandler.Owner;

            // Don't mirror buffs applied to Lorns himself.
            if (target == lorns) return;

            // Only mirror the three stat-buff types.
            StatusEffectData mirrorData = GetMirrorData(effect.effectData.effectType);
            if (mirrorData == null) return;

            // Target must be an ally of Lorns.
            if (!lorns.IsAllyOf(target)) return;

            // Ally must be within mirror range.
            if (lorns.currentTile == null || target.currentTile == null) return;
            int dist = GridManager.Instance.GetGridDistance(lorns.currentTile, target.currentTile);
            if (dist > mirrorRange) return;

            // Apply the mirror buff to Lorns at power 1, same duration.
            // Source is set to the ally (target) rather than Lorns or the original
            // source, so OnModifyEffectPower's "source == Lorns" check never fires
            // and the potency bonus is correctly excluded from mirror applications.
            StatusEffectManager.Instance.ApplyStatusEffect(
                lorns,
                mirrorData,
                source: target,          // ally is the source, not Lorns
                duration: effect.remainingDuration,
                power: 1f);
        }

        private StatusEffectData GetMirrorData(StatusEffectType effectType)
        {
            switch (effectType)
            {
                case StatusEffectType.AttackUp:  return attackUpData;
                case StatusEffectType.DefenseUp: return defenseUpData;
                case StatusEffectType.SpeedUp:   return speedUpData;
                default: return null;
            }
        }
    }
}