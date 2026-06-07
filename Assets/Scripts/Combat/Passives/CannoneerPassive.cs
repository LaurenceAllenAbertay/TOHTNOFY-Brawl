using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Cannoneer Passive (Lorns)
    ///
    /// Part 1 - Range bonus: adds rangeBonus to Unit.RangeModifier on initialise.
    /// All targeting scripts read ctx.EffectiveRange (ability.range + RangeModifier)
    /// so no ability SO data is modified.
    ///
    /// Part 2 - Fire on hit: when Lorns damages a unit, apply a Fire status effect
    /// to that unit. Subscribes to UnitManager.OnUnitDamaged which fires from
    /// DamageEffect.Apply after each hit, carrying both victim and attacker.
    /// </summary>
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
            // Part 1: apply range bonus.
            handler.Owner.RangeModifier += rangeBonus;

            // Part 2: subscribe to damage events.
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