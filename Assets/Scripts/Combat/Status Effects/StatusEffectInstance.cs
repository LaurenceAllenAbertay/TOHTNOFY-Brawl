using System;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [System.Serializable]
    public class StatusEffectInstance
    {
        public StatusEffectData effectData;
        public Unit source; 
        public Unit target; 
        public int remainingDuration;
        public int initialDuration;
        public int stackCount = 1;
        public float effectPower;  
        public object customData;   
        
        public bool hasTriggeredThisTurn = false;
        
        public bool wasTriggered = false;

        public StatusEffectInstance(StatusEffectData data, Unit source, Unit target, int duration, float power = 0)
        {
            this.effectData        = data;
            this.source            = source;
            this.target            = target;
            this.remainingDuration = duration;
            this.initialDuration   = duration;
            this.effectPower       = power;
            
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