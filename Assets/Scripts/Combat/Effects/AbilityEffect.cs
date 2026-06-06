using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Controls where in the post-animation sequence this effect fires.
    /// AbilitySequencer iterates phases in order: Displacement → Damage → StatusBuff →
    /// StatusDebuff → PostEffect.  The PreEffect phase (self-knockback) is handled by
    /// the sequencer before any camera transitions and is not part of this loop.
    /// </summary>
    public enum EffectAnimationPhase
    {
        PreEffect,      // Self-applied before camera pans to targets (e.g. self-knockback).
                        // Handled specially by AbilitySequencer; not part of the phase loop.
        Displacement,   // Knockback, Teleport, MovementEffect — the target moves.
        Damage,         // DamageEffect — hit VFX and damage numbers.
        StatusBuff,     // StatusEffect that is a positive effect.
        StatusDebuff,   // StatusEffect that is a negative effect.
        PostEffect      // QueuedActionEffect, ApplyTileEffect — fires last, no target animation.
    }

    public abstract class AbilityEffect : ScriptableObject
    {
        // ── Phase ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Which phase of the ability sequence this effect belongs to.
        /// AbilitySequencer groups effects by phase and applies them in phase order,
        /// so no concrete-type checks are needed in the sequencer.
        /// </summary>
        public virtual EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;

        // ── Target animation hint ─────────────────────────────────────────────────

        /// <summary>
        /// Which animation the sequencer should play on targets when this effect fires.
        /// Return null to suppress a target animation for this effect.
        /// </summary>
        public virtual string TargetAnimationHint => null;

        // ── Duration hint ────────────────────────────────────────────────────────

        /// <summary>
        /// Approximate wall-clock time (in seconds) the sequencer should wait after
        /// applying this effect to let its animation complete.
        /// The sequencer uses the maximum across all effects in a phase.
        /// </summary>
        public virtual float ExpectedAnimationDuration => 0f;

        // ── Self-only filter ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns true if this effect should only be applied to the caster and therefore
        /// excluded from the per-target application pass.
        /// Override in effects that are conditional on their configuration (e.g. StatusEffect
        /// where all ApplicationTargets are set to Caster).
        /// </summary>
        public virtual bool IsSelfOnly(AbilityContext ctx) => false;

        // ── Buff classification ───────────────────────────────────────────────────

        /// <summary>
        /// True when this effect is purely positive (buff). Used by AbilitySequencer
        /// to choose the correct target animation (Buff vs Debuff) without casting.
        /// StatusEffect overrides this to inspect its statusesToApply list.
        /// </summary>
        public virtual bool IsBuffEffect => false;

        /// <summary>
        /// When an effect has a caster-targeting component that runs outside the normal
        /// per-target pass, this hint tells the sequencer which animation to play on the
        /// caster unit. Return null to suppress the animation.
        /// </summary>
        public virtual string SelfCastAnimationHint => null;

        // ── Camera preview ───────────────────────────────────────────────────────

        /// <summary>
        /// Called by AbilitySequencer before applying this effect. When this returns true
        /// the sequencer pans the camera to <paramref name="focusTile"/> before calling Apply.
        /// The default returns false — override in effects like TeleportEffect that need
        /// a camera reveal before the unit moves.
        /// </summary>
        public virtual bool NeedsCameraPreview(AbilityContext ctx, out Tile focusTile)
        {
            focusTile = null;
            return false;
        }

        // ── Core ─────────────────────────────────────────────────────────────────

        public abstract void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets);
    }
}