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

        public EffectExpiryTiming expiryTiming = EffectExpiryTiming.EndOfTurn;

        [Header("Visual/Audio")]
        public GameObject applicationVFX;
        public GameObject persistentVFX;
        public GameObject removalVFX;

        public enum StackingBehavior
        {
            None,                // Cannot stack, new applications fail
            RefreshDuration,    // Reset duration to max
            AddDuration,        // Add to existing duration
            AddStacks,          // Increase stack count 
            Replace,            // Replace with new instance
            Unique              // Multiple instances can exist 
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

        public enum EffectExpiryTiming
        {
            EndOfTurn, 
            StartOfTurn 
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
        
        Stuck,
        Scared,
        Healthy,
        Saturated,
        Controlled,
        Panicked,
        Alerted,
        Intimidated,
        Taunting,
        Stunned,
        Warned,
        Shocked,
        Dizzy
    }
    
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