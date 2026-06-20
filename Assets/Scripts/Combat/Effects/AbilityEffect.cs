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

        // ── Target tile constraints ──────────────────────────────────────────────

        /// <summary>
        /// When true, the target tile must be unoccupied and passable terrain for this
        /// effect to be valid. AbilityTargeting.ShowEnterPreview uses this to colour
        /// hover highlights correctly, and SingleTargeting uses it to gate confirmation.
        /// Override in effects that move the caster to the target tile (e.g. TeleportEffect).
        /// </summary>
        public virtual bool RequiresEmptyTargetTile => false;

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

        // ── Mid-animation event dispatch ─────────────────────────────────────────

        /// <summary>
        /// When set to 0 or higher, this effect fires mid-animation via an Animation Event
        /// rather than in the post-animation phase loop.
        ///
        /// Only takes effect when the ability has suppressCameraTransitions = true (i.e.
        /// the camera stays on the caster for the full animation). The value here must match
        /// the slot fired by the corresponding AnimEvent_AbilityEffectN method on UnitAnimator
        /// — for example, midAnimationEventIndex = 1 fires when AnimEvent_AbilityEffect1 is
        /// called by the animation clip.
        ///
        /// Leave at -1 (the default) for all existing effects — they continue to fire in the
        /// normal post-animation phase loop with no change in behaviour.
        /// </summary>
        [Tooltip("Slot index (0–3) that triggers this effect mid-animation via an Animation Event. " +
                 "Only used when the ability has suppressCameraTransitions enabled. " +
                 "Set to -1 to use the normal post-animation phase loop.")]
        public int midAnimationEventIndex = -1;

        // ── Core ─────────────────────────────────────────────────────────────────

        public abstract void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets);
    }
}