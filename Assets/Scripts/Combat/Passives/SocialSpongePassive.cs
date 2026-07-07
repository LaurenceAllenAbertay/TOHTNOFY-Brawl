using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Social Sponge")]
    public class SocialSpongePassive : PassiveAbility
    {
        [Header("Buff Potency Bonus")]
        public float potencyBonus = 2f;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            System.Func<Unit, Unit, StatusEffectData, float, float> powerModifier =
                (source, target, effectData, power) =>
                {
                    if (target != handler.Owner) return power;
                    
                    if (source == handler.Owner) return power;
                    
                    if (!effectData.effectType.IsBuffType()) return power;

                    return power + potencyBonus;
                };

            StatusEffectManager.OnModifyEffectPower += powerModifier;
            RegisterCleanup(() => StatusEffectManager.OnModifyEffectPower -= powerModifier);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            base.Cleanup(handler);
        }
    }
}