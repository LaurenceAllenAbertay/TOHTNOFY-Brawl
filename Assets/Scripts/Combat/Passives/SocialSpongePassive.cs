using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Social Sponge Passive (Brodie)
    ///
    /// When any ally applies a buff status effect to Brodie, the effectPower of
    /// that application is increased by a flat bonus before the instance is created.
    /// Self-applied buffs (Brodie buffing himself) are intentionally excluded —
    /// the description specifies "buffed by allies."
    ///
    /// Hooks into StatusEffectManager.OnModifyEffectPower, which runs before
    /// the StatusEffectInstance is created, so the boosted power is baked in
    /// from the moment the effect is applied — no separate patching needed.
    ///
    /// Works for both player-controlled and AI-controlled units because
    /// OnModifyEffectPower fires from StatusEffectManager.ApplyStatusEffect,
    /// which is the single code path all buff applications go through.
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Social Sponge")]
    public class SocialSpongePassive : PassiveAbility
    {
        [Header("Buff Potency Bonus")]
        [Tooltip("Flat amount added to effectPower when an ally applies any buff to Brodie.")]
        public float potencyBonus = 2f;

        // Stored delegate reference so we can unsubscribe with the exact same instance.
        private System.Func<Unit, Unit, StatusEffectData, float, float> powerModifierHandler;

        // Cached so the lambda can safely reference it without capturing a stale local.
        private PassiveAbilityHandler cachedHandler;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            cachedHandler = handler;

            powerModifierHandler = (source, target, effectData, power) =>
            {
                // Only boost effects being applied TO Brodie.
                if (target != cachedHandler.Owner) return power;

                // Exclude self-application — the passive only triggers when an ally buffs Brodie.
                if (source == cachedHandler.Owner) return power;

                // Only boost buff-type effects, not debuffs or damage-over-time.
                if (!IsBuffEffectType(effectData.effectType)) return power;

                return power + potencyBonus;
            };

            StatusEffectManager.OnModifyEffectPower += powerModifierHandler;
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            if (powerModifierHandler != null)
            {
                StatusEffectManager.OnModifyEffectPower -= powerModifierHandler;
                powerModifierHandler = null;
            }

            cachedHandler = null;
        }

        /// <summary>
        /// Mirrors the buff classification used by AbilitySequencer.IsBuffEffectType
        /// so the two stay in sync. Update both if new buff types are added.
        /// </summary>
        private static bool IsBuffEffectType(StatusEffectType effectType)
        {
            switch (effectType)
            {
                case StatusEffectType.Shielded:
                case StatusEffectType.Guarded:
                case StatusEffectType.Untargetable:
                case StatusEffectType.AttackUp:
                case StatusEffectType.DefenseUp:
                case StatusEffectType.SpeedUp:
                case StatusEffectType.Hastened:
                case StatusEffectType.Urged:
                case StatusEffectType.Healthy:
                case StatusEffectType.Saturated:
                case StatusEffectType.Alerted:
                    return true;
                default:
                    return false;
            }
        }
    }
}