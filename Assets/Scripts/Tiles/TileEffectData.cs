using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Abstract base for all tile effects. Mirrors AbilityEffect / StatusEffectData in pattern.
    /// Create a ScriptableObject subclass per effect type, wire it up in the inspector, and
    /// compose tile behaviours without modifying Tile.cs.
    ///
    /// Apply() is a coroutine so effects can drive visual feedback (hurt animations, VFX,
    /// camera cuts) before and after their gameplay change. EndTurnSequence in TurnManager
    /// yields on TriggerEnvironmentEffects, which yields on each tile's TriggerEffects coroutine,
    /// so StartNextTurn() never fires until all tile effect feedback has finished.
    /// </summary>
    public abstract class TileEffectData : ScriptableObject
    {
        [Header("Basic Info")]
        [Tooltip("Human-readable name shown in UI tooltips.")]
        public string effectName = "Tile Effect";

        [Tooltip("Short description shown in tile inspect UI.")]
        [TextArea(2, 4)]
        public string description = "";

        [Tooltip("Icon displayed in tile tooltip UI. Mirrors StatusEffectData.icon.")]
        public Sprite icon;

        [Tooltip("Tint used for UI elements and VFX colour variants. Mirrors StatusEffectData.effectColor.")]
        public Color effectColor = Color.white;

        [Header("Visual / Audio")]
        [Tooltip("Spawned on the tile when this effect is first applied.")]
        public GameObject applicationVFX;

        [Tooltip("Spawned on the tile and persists for the effect's duration.")]
        public GameObject persistentVFX;

        [Tooltip("Spawned on the tile when this effect expires or is dispelled.")]
        public GameObject removalVFX;

        /// <summary>
        /// Execute the effect. Called once per trigger from Tile.TriggerEffects() at round end.
        /// Returns IEnumerator — yield on animations/VFX before or after the gameplay change.
        /// Simple effects do their work and yield break immediately.
        /// ctx.unitOnTile may be null — always null-check before applying unit-specific logic.
        /// </summary>
        public abstract IEnumerator Apply(TileEffectContext ctx);

        /// <summary>
        /// Returns a flat danger value used by the AI in ActionPlan.CalculateDangerAtPosition()
        /// when scoring whether to move onto this tile. Higher = more dangerous.
        /// Matches the threat scale used in existing ability threat calculations.
        /// Override in each subclass so the AI benefits automatically from every new effect type.
        /// </summary>
        public abstract float GetAIDangerValue();

        // ── VFX Helpers ───────────────────────────────────────────────────────────
        // Called by Tile.cs at the right lifecycle moments. Subclasses can also call
        // these directly from Apply() if they need custom timing (e.g. spawn a hit VFX
        // at the exact moment damage lands rather than on application).

        /// <summary>
        /// Spawns the applicationVFX at the tile position. Called by Tile.AddEffect.
        /// One-shot: spawned and not tracked — use for impact flashes, puffs, etc.
        /// Safe to call when applicationVFX is null (no-op).
        /// </summary>
        public void SpawnApplicationVFX(Vector3 position)
        {
            if (applicationVFX != null)
                Object.Instantiate(applicationVFX, position, Quaternion.identity);
        }

        /// <summary>
        /// Spawns the persistentVFX at the tile position and returns the instance so
        /// the caller can destroy it when the effect expires. Called by Tile.AddEffect.
        /// Returns null when persistentVFX is not assigned.
        /// </summary>
        public GameObject SpawnPersistentVFX(Vector3 position)
        {
            if (persistentVFX == null) return null;
            return Object.Instantiate(persistentVFX, position, Quaternion.identity);
        }

        /// <summary>
        /// Spawns the removalVFX at the tile position. Called by Tile when an effect expires
        /// or is dispelled via ClearAllEffects(). One-shot: spawned and not tracked.
        /// Safe to call when removalVFX is null (no-op).
        /// </summary>
        public void SpawnRemovalVFX(Vector3 position)
        {
            if (removalVFX != null)
                Object.Instantiate(removalVFX, position, Quaternion.identity);
        }
    }
}