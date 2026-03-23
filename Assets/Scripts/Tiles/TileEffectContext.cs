namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Data bag passed into TileEffectData.Apply(), mirroring AbilityContext.
    /// Contains all information a tile effect needs to execute its logic.
    /// </summary>
    public class TileEffectContext
    {
        /// <summary>The tile this effect is being triggered on.</summary>
        public Tile tile;

        /// <summary>The unit currently standing on the tile, or null if empty.</summary>
        public Unit unitOnTile;

        /// <summary>The unit who originally applied this effect (e.g. the caster). Can be null.</summary>
        public Unit applier;

        /// <summary>
        /// The magnitude of this specific effect instance (damage amount, heal amount, etc.).
        /// Set from TileEffectInstance.effectPower at trigger time.
        /// </summary>
        public float effectPower;

        /// <summary>The current round number, for effects that scale or behave differently over time.</summary>
        public int currentRound;
    }
}