using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Runtime instance of a tile effect. Holds the data asset plus per-instance state.
    /// Mirrors StatusEffectInstance — one data asset, many possible live instances.
    /// </summary>
    [System.Serializable]
    public class TileEffectInstance
    {
        /// <summary>The ScriptableObject defining what this effect does.</summary>
        public TileEffectData effectData;

        /// <summary>The unit that applied this effect. Can be null for environmental sources.</summary>
        public Unit applier;

        /// <summary>How many more round-end triggers remain before this effect expires.</summary>
        public int remainingRounds;

        /// <summary>
        /// Magnitude of this instance (damage per round, heal per round, stat change, etc.).
        /// Passed into TileEffectContext.effectPower at trigger time.
        /// </summary>
        public float effectPower;

        /// <summary>
        /// Open slot for effects that need per-instance state beyond effectPower.
        /// Mirrors StatusEffectInstance.customData — e.g. a tile that counts how many units
        /// have walked through it before detonating could store that counter here.
        /// </summary>
        public object customData;

        public bool IsExpired => remainingRounds <= 0;

        public TileEffectInstance(TileEffectData data, Unit applier, int rounds, float power)
        {
            this.effectData      = data;
            this.applier         = applier;
            this.remainingRounds = rounds;
            this.effectPower     = power;
        }

        /// <summary>
        /// Refreshes duration if the new value is longer than the current remaining rounds.
        /// Prevents a short re-application from overwriting a longer active one.
        /// </summary>
        public void RefreshDuration(int newRounds)
        {
            remainingRounds = Mathf.Max(remainingRounds, newRounds);
        }
    }
}