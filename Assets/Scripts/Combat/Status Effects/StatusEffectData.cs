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
        Bleed = 0,      // Snowballing DOT — damage increases each turn it's active
        Poison = 1,     // Opposite of Bleed — starts higher, damage decreases each turn
        Fire = 2,       // Consistent DOT — same damage every turn

        // Delayed Damage
        Shocked = 29,   // Bursts 1 turn after activating, also hits allies within 3x3 tiles

        // Defensive
        Shielded = 3,       // Prevents all damage, then breaks
        Guarded = 4,        // Damage taken is redirected to whoever applied the effect
        Untargetable = 5,   // Cannot be targeted by enemies
        Immune = 6,         // Clears and prevents all status effects
        Invulnerable = 31,  // Prevents all damage taken
        Warned = 28,        // Dodges attacks that would've otherwise hit

        // Stat Modifiers
        AttackUp = 7,       // +10% damage dealt
        DefenseUp = 8,      // -10% damage taken from most sources
        SpeedUp = 9,        // +1 movement
        AttackDown = 10,    // -10% damage dealt
        DefenseDown = 11,   // +10% damage taken from most sources
        SpeedDown = 12,     // -1 movement

        // Health
        Healthy = 20,    // Max health increase
        Unhealthy = 32,  // Max health decrease
        Saturated = 21,  // More healing from healing sources

        // Turn Order
        Hastened = 13,  // Skips the affected character to next in turn order
        Urged = 14,     // Inflicted by The Urge — effect not yet defined/implemented

        // Movement
        Encumbered = 17,  // Prevents jumping
        Stuck = 18,       // Prevents movement

        // Crowd Control
        Scared = 19,   // Prevents attacking
        Stunned = 27,  // Skips the affected character's turn
        Dizzy = 30,    // Forces random movement on their turn

        // Targeting
        Intimidated = 25,  // Enemies won't target whoever intimidated them
        Taunting = 26,     // Enemies are more likely to target you
        Charmed = 33       // Enemies will fight for the opposite team (not yet implemented)
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
                case StatusEffectType.Invulnerable:
                case StatusEffectType.Warned:
                case StatusEffectType.AttackUp:
                case StatusEffectType.DefenseUp:
                case StatusEffectType.SpeedUp:
                case StatusEffectType.Hastened:
                case StatusEffectType.Urged:
                case StatusEffectType.Healthy:
                case StatusEffectType.Saturated:
                    return true;
                default:
                    return false;
            }
        }
    }
}