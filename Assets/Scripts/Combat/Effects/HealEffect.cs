using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// AbilityEffect subclass that restores health to its targets.
    ///
    /// Deliberately bypasses Unit.ReceiveDamage — healing should never trigger
    /// Shielded / Immune / Guarded checks, never interact with defence stats,
    /// and can never kill a unit. The pattern mirrors RecoilDamageEffect, which
    /// also writes currentHealth directly and calls Unit.NotifyHealthChanged so
    /// the health bar updates correctly.
    ///
    /// Two inspector fields drive the amount:
    ///   flatHeal       — a fixed HP value added to every target.
    ///   attackScaling  — optional multiplier on the caster's currentAttack (default 0 = off).
    ///
    /// Final heal = flatHeal + (caster.currentAttack * attackScaling), clamped to maxHealth.
    /// The Mega Mug has 0 attack, so attackScaling is meaningless for it — just use flatHeal.
    /// The field is there so future healing units (e.g. a support character) can scale off attack.
    ///
    /// Phase: StatusBuff — targets play their Buff animation and the sequencer waits
    /// 0.6 s (matching StatusEffect) before moving on. This gives the heal VFX time
    /// to read correctly before the camera moves to the next target.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Effects > Heal Effect
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Heal Effect")]
    public class HealEffect : AbilityEffect
    {
        [Header("Heal Amount")]
        [Tooltip("Flat HP restored to every target, regardless of stats.")]
        [SerializeField] private int flatHeal = 10;

        [Tooltip("Optional multiplier on the caster's currentAttack added on top of flatHeal. " +
                 "Set to 0 (default) for a purely flat heal — useful for stat-less neutral units " +
                 "like the Mega Mug. Set > 0 for healing characters whose output should scale " +
                 "with their attack stat.")]
        [SerializeField] [Range(0f, 2f)] private float attackScaling = 0f;

        // ── AbilityEffect overrides ───────────────────────────────────────────

        /// <summary>
        /// StatusBuff phase — runs after Displacement and Damage, alongside other buffs.
        /// AbilitySequencer plays the Buff animation on each target before this fires.
        /// </summary>
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.StatusBuff;

        /// <summary>
        /// Tells AbilitySequencer to play the "Buff" animation on each target.
        /// Falls back to "Hurt" gracefully in PlayTargetAnimation if no Buff state exists.
        /// </summary>
        public override string TargetAnimationHint => "Buff";

        /// <summary>
        /// Matches StatusEffect's duration so the sequencer waits long enough for the
        /// Buff animation to finish before panning to the next target or returning control.
        /// </summary>
        public override float ExpectedAnimationDuration => 0.6f;

        /// <summary>
        /// Marks this as a buff so AbilitySequencer sorts it into the buff pass and
        /// AIPlanner's AppliesBuff check recognises healing abilities correctly.
        /// </summary>
        public override bool IsBuffEffect => true;

        // ── Apply ─────────────────────────────────────────────────────────────

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (targets == null) return;

            // Compute how much to heal — flat amount plus optional attack scaling.
            int healAmount = flatHeal;
            if (attackScaling > 0f && ctx?.caster != null)
                healAmount += Mathf.RoundToInt(ctx.caster.currentAttack * attackScaling);

            if (healAmount <= 0) return;

            foreach (var target in targets)
            {
                if (target == null || target.IsDead) continue;

                // Bodies cannot be healed — they are frozen on their death frame.
                if (target.IsBody) continue;

                // Determine the health cap. characterData holds the authoritative maxHealth;
                // fall back to currentHealth (no-op heal) if somehow characterData is missing.
                int maxHealth = target.characterData != null
                    ? target.characterData.maxHealth
                    : target.currentHealth;

                int before = target.currentHealth;
                target.currentHealth = Mathf.Min(target.currentHealth + healAmount, maxHealth);
                int actualHeal = target.currentHealth - before;

                if (actualHeal <= 0) continue; // Already at full health — skip event.

                // Notify the health bar and any other OnHealthChanged subscribers.
                // We write currentHealth directly (bypassing ReceiveDamage), so we must
                // fire this manually — the same pattern used by RecoilDamageEffect.
                Unit.NotifyHealthChanged(target);

                Debug.Log($"[HealEffect] {target.name} restored {actualHeal} HP " +
                          $"({before} → {target.currentHealth} / {maxHealth}).");
            }
        }
    }
}