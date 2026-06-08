using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Static data bag written by DebugLobbyController and read by DebugMapSpawner.
    ///
    /// Carries only the spawn identity for each unit — which CharacterData to use
    /// and which prefab path to load. Ability and passive data for player units is
    /// written directly into UnitLoadoutManager by the lobby and read from there by
    /// all in-combat systems. Enemy ability data lives on the EnemyLoadout component
    /// on each enemy prefab and is never stored here.
    ///
    /// Static fields survive SceneManager.LoadScene within the same Unity session,
    /// so no DontDestroyOnLoad is needed for this carrier object.
    /// </summary>
    public static class DebugSessionConfig
    {
        // ── Player spawn list ─────────────────────────────────────────────────

        /// <summary>
        /// One entry per player unit to spawn, in spawn-point order.
        /// The lobby writes this; DebugMapSpawner reads it.
        /// </summary>
        public static readonly List<PlayerSpawnConfig> PlayerSpawns = new List<PlayerSpawnConfig>();

        // ── Enemy spawn list ──────────────────────────────────────────────────

        /// <summary>
        /// One entry per enemy unit to spawn, in spawn-point order.
        /// Which prefab to use determines which EnemyLoadout component (and therefore
        /// which abilities) that enemy instance gets — chosen in the lobby per slot.
        /// </summary>
        public static readonly List<EnemySpawnConfig> EnemySpawns = new List<EnemySpawnConfig>();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Clears both lists so the lobby can repopulate from scratch.</summary>
        public static void Clear()
        {
            PlayerSpawns.Clear();
            EnemySpawns.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawn identity for one player unit.
    /// Ability and passive data is NOT stored here — it lives in UnitLoadoutManager,
    /// written by the lobby before the scene loads.
    /// </summary>
    [System.Serializable]
    public class PlayerSpawnConfig
    {
        /// <summary>
        /// The CharacterData asset for this player unit.
        /// Used as the key into UnitLoadoutManager to retrieve the equipped loadout,
        /// and assigned to the spawned unit's characterData field.
        /// </summary>
        public CharacterData characterData;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawn identity for one enemy unit.
    /// The prefab reference determines which EnemyLoadout component the instance
    /// gets — two enemies with the same CharacterData but different prefabs can
    /// therefore have different ability kits.
    /// </summary>
    [System.Serializable]
    public class EnemySpawnConfig
    {
        /// <summary>The CharacterData asset for this enemy.</summary>
        public CharacterData characterData;

        /// <summary>
        /// The specific enemy prefab to instantiate.
        /// The prefab must have an EnemyUnit component and an EnemyLoadout component.
        /// Stored here because the lobby lets the player pick which enemy prefab fills
        /// each slot — this is how two slots can both be "Kallper" but use different kits.
        /// </summary>
        public GameObject prefab;
    }
}