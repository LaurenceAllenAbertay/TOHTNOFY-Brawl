using System;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [System.Serializable]
    public class StatusEffectInstance
    {
        public StatusEffectData effectData;
        public Unit source;          // Who applied this effect
        public Unit target;          // Who has this effect
        public int remainingDuration;
        /// <summary>
        /// The duration this effect was applied with. Immutable after creation.
        /// Available for any effect that needs to know its original lifetime.
        /// </summary>
        public int initialDuration;
        public int stackCount = 1;
        public float effectPower;     // Generic power value (damage, heal amount, stat change, etc.)
        public object customData;     // For complex effects that need extra state

        // Timing flags
        public bool hasTriggeredThisTurn = false;

        /// <summary>
        /// Set to true when this effect activates its special behaviour during gameplay
        /// (e.g. Warned fires its dodge). Checked at expiry to decide whether to apply
        /// a fallback consequence (e.g. DefenseDown if Warned never triggered).
        /// </summary>
        public bool wasTriggered = false;

        public StatusEffectInstance(StatusEffectData data, Unit source, Unit target, int duration, float power = 0)
        {
            this.effectData        = data;
            this.source            = source;
            this.target            = target;
            this.remainingDuration = duration;
            this.initialDuration   = duration;
            this.effectPower       = power;

            // For AddStacks effects (Bleeding, Poison), effectPower IS the initial stack count.
            // All other effects default to 1.
            if (data != null && data.stackingBehavior == StatusEffectData.StackingBehavior.AddStacks)
                this.stackCount = Mathf.Max(1, Mathf.RoundToInt(power));
        }

        public bool IsExpired => remainingDuration <= 0;

        public void RefreshDuration(int newDuration)
        {
            remainingDuration = Mathf.Max(remainingDuration, newDuration);
        }
    }
}