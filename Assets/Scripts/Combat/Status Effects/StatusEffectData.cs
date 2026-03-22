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

        // Defensive
        Shielded,
        Guarded,
        Untargetable,

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
        Alerted
    }
}