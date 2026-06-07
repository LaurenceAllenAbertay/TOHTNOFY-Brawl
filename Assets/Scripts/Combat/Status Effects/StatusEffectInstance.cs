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
        /// Used by escalating damage formulas (e.g. Poison) to determine which tick
        /// is currently firing without relying on fragile proxies like asset name length.
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
        }

        public bool IsExpired => remainingDuration <= 0;

        public void RefreshDuration(int newDuration)
        {
            remainingDuration = Mathf.Max(remainingDuration, newDuration);
        }
    }
}