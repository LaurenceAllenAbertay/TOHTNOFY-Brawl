using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [System.Serializable]
    public class TileEffectInstance
    {
        public TileEffectData effectData;

        public Unit applier;

        public int remainingRounds;
        
        public float effectPower;
        
        public object customData;

        public bool IsExpired => remainingRounds <= 0;

        public TileEffectInstance(TileEffectData data, Unit applier, int rounds, float power)
        {
            this.effectData      = data;
            this.applier         = applier;
            this.remainingRounds = rounds;
            this.effectPower     = power;
        }
        
        public void RefreshDuration(int newRounds)
        {
            remainingRounds = Mathf.Max(remainingRounds, newRounds);
        }
    }
}