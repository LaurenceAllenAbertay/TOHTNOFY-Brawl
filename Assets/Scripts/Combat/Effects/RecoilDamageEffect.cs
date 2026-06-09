using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Deals a fraction of the damage resolved by DamageEffect back to the caster.
    ///
    /// Relies on AbilityContext.LastResolvedDamage, which DamageEffect accumulates during
    /// its Apply call. Place this effect after DamageEffect in the Ability's effects list
    /// so the phase ordering guarantees DamageEffect fires first (Damage phase runs before
    /// PostEffect phase).
    ///
    /// The recoil bypasses the caster's own defence and status effects like Immune/Shielded —
    /// this is intentional: the caster is willingly hurting themselves as a trade-off.
    /// If you want defence to apply, call caster.ReceiveDamage instead of the direct subtract.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Effects > Recoil Damage Effect
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Recoil Damage Effect")]
    public class RecoilDamageEffect : AbilityEffect
    {
        [Tooltip("Fraction of the damage dealt to the target that is reflected back to the caster. " +
                 "0.25 = 25% recoil (e.g. No Survivors).")]
        [Range(0f, 1f)]
        public float recoilFraction = 0.25f;

        // Fires in PostEffect so DamageEffect (Damage phase) is guaranteed to have run first,
        // meaning LastResolvedDamage is already populated when we read it.
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;

        // Play the Hurt animation on the caster to sell the recoil visually.
        // SelfCastAnimationHint is picked up by AbilitySequencer.HandleRemainingEffects
        // and plays on the caster unit before this effect's Apply is called.
        public override string SelfCastAnimationHint => "Debuff";

        public override bool IsSelfOnly(AbilityContext ctx) => true;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;

            int resolvedDamage = ctx.LastResolvedDamage;
            if (resolvedDamage <= 0) return;

            int recoil = Mathf.Max(1, Mathf.RoundToInt(resolvedDamage * recoilFraction));

            // Recoil bypasses defence — the caster is hurting themselves deliberately.
            // Using the direct field rather than ReceiveDamage so Immune/Shield/Alerted
            // don't absorb the self-inflicted cost.
            ctx.caster.currentHealth -= recoil;
            Debug.Log($"[RecoilDamageEffect] {ctx.caster.name} took {recoil} recoil " +
                      $"({recoilFraction * 100}% of {resolvedDamage} damage dealt).");

            // Notify UI — direct subtract bypasses ReceiveDamage so we fire the event manually.
            Unit.NotifyHealthChanged(ctx.caster);

            if (ctx.caster.currentHealth <= 0)
            {
                Debug.Log($"[RecoilDamageEffect] {ctx.caster.name} was defeated by recoil.");
                ctx.caster.Die(killer: null); // recoil is self-inflicted — no external killer
            }
        }
    }
}