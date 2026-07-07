using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Cannoneer")]
    public class CannoneerPassive : PassiveAbility
    {
        [Header("Range Bonus")]
        public int rangeBonus = 1;

        [Header("Fire On Hit")]
        public StatusEffectData fireData;
        public float firePower = 2f;
        public int fireDuration = 2;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            handler.Owner.RangeModifier += rangeBonus;
            
            System.Action<Unit, Unit> onDamaged = (victim, attacker) =>
            {
                if (attacker != handler.Owner) return;
                if (victim == null || StatusEffectManager.Instance == null) return;
                if (fireData == null) return;

                StatusEffectManager.Instance.ApplyStatusEffect(
                    victim, fireData, handler.Owner, fireDuration, firePower);
            };
            UnitManager.OnUnitDamaged += onDamaged;
            RegisterCleanup(() => UnitManager.OnUnitDamaged -= onDamaged);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            handler.Owner.RangeModifier -= rangeBonus;
            base.Cleanup(handler);
        }
    }
}