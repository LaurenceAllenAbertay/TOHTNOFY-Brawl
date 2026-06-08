using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Status Effect Data")]
    public class StatusEffectData : ScriptableObject
    {
        [Header("Basic Info")]
        public string effectName = "Unnamed Effect";
        public string description;
        public Sprite icon;
        public Color effectColor = Color.white;

        [Header("Effect Type")]
        public StatusEffectType effectType;

        [Header("Stacking Behavior")]
        public StackingBehavior stackingBehavior = StackingBehavior.RefreshDuration;
        public int maxStacks = 1;

        [Header("Timing")]
        public EffectTriggerTiming triggerTiming = EffectTriggerTiming.StartOfTurn;
        public bool triggersOnApplication = false;

        [Header("Visual/Audio")]
        public GameObject applicationVFX;
        public GameObject persistentVFX;
        public GameObject removalVFX;

        public enum StackingBehavior
        {
            None,               // Cannot stack, new applications fail
            RefreshDuration,    // Reset duration to max
            AddDuration,        // Add to existing duration
            AddStacks,          // Increase stack count (for Bleeding)
            Replace,            // Replace with new instance
            Unique              // Multiple instances can exist (different sources)
        }

        public enum EffectTriggerTiming
        {
            StartOfTurn,
            EndOfTurn,
            BeforeDamage,
            AfterDamage,
            OnMove,
            OnAttack,
            Continuous
        }
    }

    public enum StatusEffectType
    {
        // Damage Over Time
        Bleeding,
        Poison,
        Fire,

        // Defensive
        Shielded,
        Guarded,
        Untargetable,

        // Immune: blocks all incoming damage for the duration.
        // Application chance drops 75% per consecutive use on the same target.
        // Counter resets when the unit goes a full turn without being made Immune.
        Immune,

        // Stat Modifiers
        AttackUp,
        DefenseUp,
        SpeedUp,
        AttackDown,
        DefenseDown,
        SpeedDown,

        // Turn Order
        Hastened,
        Urged,
        Distracted,

        // Movement
        Ensnared,
        Encumbered,

        // Healing
        Healthy,
        Saturated,

        // Control
        Controlled,
        Panicked,
        Alerted,

        // Targeting Restrictions
        // The affected unit cannot choose the source of this effect as a target.
        // Does not prevent the source from being hit by AOE targeting another unit.
        Intimidated,

        // Targeting Priority
        // Enemy AI receives a scoring bonus when evaluating this unit as a target.
        // Does not force targeting -- other factors like distance can still outweigh the bonus.
        Taunting,

        // Stun: skips the affected unit's turn. stackCount = turns remaining.
        // Application chance drops 75% per consecutive use on the same target.
        // Counter resets when the unit goes a full turn without being stunned.
        Stunned,

        // Warned: the affected ally attempts to dodge the next incoming targeted attack.
        // Before the attacker's animation plays, the Warned unit steps to an adjacent free tile.
        // If the new tile is outside the ability's traversal, the attack misses entirely.
        // If it expires without being triggered (no attack came), applies DefenseDown instead.
        Warned,

        // Shocked: inflicted by Wiring Fault.
        // The precise gameplay effect (e.g. skip turn, stat penalty) is configured on the
        // StatusEffectData asset — no hard-coded behaviour is required here.
        Shocked
    }

    /// <summary>
    /// Returns true when <paramref name="effectType"/> is a positive (buff) effect.
    /// This is the single authoritative definition of "buff" for the whole codebase.
    /// Add new buff types here — AbilitySequencer and SocialSpongePassive both
    /// delegate to this method, so one edit covers both.
    /// </summary>
    public static class StatusEffectTypeExtensions
    {
        public static bool IsBuffType(this StatusEffectType effectType)
        {
            switch (effectType)
            {
                case StatusEffectType.Shielded:
                case StatusEffectType.Guarded:
                case StatusEffectType.Untargetable:
                case StatusEffectType.Immune:
                case StatusEffectType.Warned:
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