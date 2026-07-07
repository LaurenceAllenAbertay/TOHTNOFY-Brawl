using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Rascal")]
    public class RascalPassive : PassiveAbility
    {
        [Header("Defense Boost")]
        public StatusEffectData defenseUpData;

        public float defensePower = 2f;
        
        public int defenseDuration = 2;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            System.Action<Unit, Unit> onDamaged = (victim, attacker) =>
            {
                if (attacker != handler.Owner) return;
                if (defenseUpData == null || StatusEffectManager.Instance == null) return;
                
                StatusEffectManager.Instance.ApplyStatusEffect(
                    handler.Owner,
                    defenseUpData,
                    source: handler.Owner,
                    duration: defenseDuration,
                    power: defensePower);
            };

            UnitManager.OnUnitDamaged += onDamaged;
            RegisterCleanup(() => UnitManager.OnUnitDamaged -= onDamaged);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            base.Cleanup(handler);
        }
    }
}