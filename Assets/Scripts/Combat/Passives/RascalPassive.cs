using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Rascal Passive
    ///
    /// When Rascal inflicts damage on any unit, he gains a DefenseUp status effect
    /// on himself for 1 turn. The buff is applied to the attacker (Rascal), not the
    /// victim, and is re-applied (refreshed) on every hit thanks to RefreshDuration
    /// stacking behaviour on the DefenseUp StatusEffectData asset.
    ///
    /// Duration note: the StatusEffectManager decrements durations at end-of-turn.
    /// A duration of 1 would expire at the end of Rascal's own turn, giving zero
    /// defensive benefit. defenseDuration defaults to 2 so the buff survives through
    /// the next enemy turn — which is the "1 turn" the description refers to.
    ///
    /// Hooks into UnitManager.OnUnitDamaged, which is fired by DamageEffect.Apply
    /// after every hit. This means the passive works identically for player-controlled
    /// and AI-controlled use of Rascal.
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Rascal")]
    public class RascalPassive : PassiveAbility
    {
        [Header("Defense Boost")]
        [Tooltip("DefenseUp StatusEffectData asset to apply after dealing damage.")]
        public StatusEffectData defenseUpData;

        [Tooltip("How much defense is added (effectPower passed to the status effect).")]
        public float defensePower = 2f;

        [Tooltip(
            "Duration passed to ApplyStatusEffect. Defaults to 2 because the turn counter " +
            "ticks at end-of-turn: 2 survives Rascal's current turn and remains active " +
            "through the following enemy turn, matching the '1 turn' description.")]
        public int defenseDuration = 2;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            System.Action<Unit, Unit> onDamaged = (victim, attacker) =>
            {
                // Only react when Rascal is the one dealing damage.
                if (attacker != handler.Owner) return;
                if (defenseUpData == null || StatusEffectManager.Instance == null) return;

                // Apply the defense boost to Rascal himself.
                // Source is also Rascal so OnModifyEffectPower passives that key on
                // source (e.g. Mastermind) won't accidentally boost this.
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