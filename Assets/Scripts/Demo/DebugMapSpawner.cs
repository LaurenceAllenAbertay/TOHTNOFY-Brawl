using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Reads DebugSessionConfig and spawns the correct player and enemy units into
    /// the debug map scene before UnitManager and TurnManager initialise.
    ///
    /// ── What this spawner does ────────────────────────────────────────────────
    /// • Player units: spawns the correct prefab, assigns CharacterData.
    ///   Ability and passive data is already in UnitLoadoutManager (written by the lobby)
    ///   and is read from there by all in-combat systems — no extra work needed here.
    ///
    /// • Enemy units: instantiates the prefab chosen in the lobby. The EnemyLoadout
    ///   component on that prefab carries the designer-set abilities and passive.
    ///   No cloning or data injection needed — it's all on the prefab already.
    ///
    /// ── Execution Order ──────────────────────────────────────────────────────
    /// Set in Project Settings → Script Execution Order:
    ///   GridManager      → -300
    ///   DebugMapSpawner  → -200
    ///   UnitManager      → -100
    ///   TurnManager      → 0  (default)
    ///
    /// ── Spawn Tile Strategy ──────────────────────────────────────────────────
    /// Drag Tile scene objects directly into the player/enemy spawn tile arrays.
    /// Units are instantiated at tile.position + spawnOffset, then Unit.Awake()
    /// snaps each unit to that same tile via GridManager.GetTileAtPosition().
    /// Each character's prefab is read from CharacterData.prefab — assign it there.
    /// </summary>
    public class DebugMapSpawner : MonoBehaviour
    {
        // ── Inspector wiring ──────────────────────────────────────────────────

        [Header("Spawn Offset")]
        [Tooltip("World-space offset added to each tile's position when spawning a unit. " +
                 "Default (0, 1.5, 0) places the unit visually above the tile surface.")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Spawn Tiles")]
        [Tooltip("Drag Tile scene objects here — one per player slot, in spawn order.")]
        [SerializeField] private Tile[] playerSpawnTiles;

        [Tooltip("Drag Tile scene objects here — one per enemy slot, in spawn order.")]
        [SerializeField] private Tile[] enemySpawnTiles;

        [Header("Fallback (for running Debug scene without lobby)")]
        [Tooltip("CharacterData assets to spawn as players when no lobby config exists.")]
        [SerializeField] private CharacterData[] fallbackPlayerCharacters;

        [Tooltip("Enemy prefab(s) to spawn when no lobby config exists. " +
                 "Must have EnemyUnit + EnemyLoadout components.")]
        [SerializeField] private GameObject[] fallbackEnemyPrefabs;

        // ── Awake ─────────────────────────────────────────────────────────────

        private void Awake()
        {
            bool hasLobbyData = DebugSessionConfig.PlayerSpawns.Count > 0
                             || DebugSessionConfig.EnemySpawns.Count  > 0;

            if (hasLobbyData)
                SpawnFromConfig();
            else
                SpawnFallback();
        }

        // ── Spawn from lobby config ───────────────────────────────────────────

        private void SpawnFromConfig()
        {
            // Player units
            var playerSpawns = DebugSessionConfig.PlayerSpawns;
            for (int i = 0; i < playerSpawns.Count; i++)
            {
                if (i >= playerSpawnTiles.Length)
                {
                    Debug.LogWarning($"[DebugMapSpawner] No spawn tile for player {i} — add more Player Spawn Tiles.");
                    break;
                }

                var config = playerSpawns[i];
                if (config.characterData == null)
                {
                    Debug.LogWarning($"[DebugMapSpawner] Player spawn {i} has no CharacterData — skipped.");
                    continue;
                }

                SpawnPlayerUnit(config.characterData.prefab, config.characterData, playerSpawnTiles[i]);
            }

            // Enemy units — each carries its own prefab reference from the lobby
            var enemySpawns = DebugSessionConfig.EnemySpawns;
            for (int i = 0; i < enemySpawns.Count; i++)
            {
                if (i >= enemySpawnTiles.Length)
                {
                    Debug.LogWarning($"[DebugMapSpawner] No spawn tile for enemy {i} — add more Enemy Spawn Tiles.");
                    break;
                }

                var config = enemySpawns[i];
                if (config.prefab == null)
                {
                    Debug.LogWarning($"[DebugMapSpawner] Enemy spawn {i} has no prefab — skipped.");
                    continue;
                }

                SpawnEnemyUnit(config.prefab, enemySpawnTiles[i]);
            }
        }

        // ── Fallback spawn ────────────────────────────────────────────────────

        private void SpawnFallback()
        {
            Debug.Log("[DebugMapSpawner] No lobby config found — using fallback spawn.");

            if (fallbackPlayerCharacters != null)
            {
                for (int i = 0; i < fallbackPlayerCharacters.Length; i++)
                {
                    if (i >= playerSpawnTiles.Length) break;
                    if (fallbackPlayerCharacters[i] == null) continue;

                    // Register a default loadout for fallback players so in-combat systems
                    // don't warn about missing loadout entries.
                    if (UnitLoadoutManager.Instance != null &&
                        !UnitLoadoutManager.Instance.HasPlayerLoadout(fallbackPlayerCharacters[i]))
                    {
                        // Use the first 3 availableAbilities as the fallback loadout
                        var pool = fallbackPlayerCharacters[i].availableAbilities;
                        var fallbackAbilities = new Ability[3];
                        if (pool != null)
                            for (int s = 0; s < 3 && s < pool.Length; s++)
                                fallbackAbilities[s] = pool[s];

                        UnitLoadoutManager.Instance.SetPlayerLoadout(
                            fallbackPlayerCharacters[i], fallbackAbilities, passive: null);
                    }

                    SpawnPlayerUnit(fallbackPlayerCharacters[i].prefab, fallbackPlayerCharacters[i], playerSpawnTiles[i]);
                }
            }

            if (fallbackEnemyPrefabs != null)
            {
                for (int i = 0; i < fallbackEnemyPrefabs.Length; i++)
                {
                    if (i >= enemySpawnTiles.Length) break;
                    if (fallbackEnemyPrefabs[i] == null) continue;
                    SpawnEnemyUnit(fallbackEnemyPrefabs[i], enemySpawnTiles[i]);
                }
            }
        }

        // ── Spawn helpers ─────────────────────────────────────────────────────

        /// <summary>
        /// Spawns a player unit at the given tile, assigns CharacterData before Awake fires.
        /// The prefab is instantiated inactive so Unit.Awake() doesn't run until after
        /// characterData is set — otherwise stats and tile-snap would use a null CharacterData.
        /// Ability and passive data is read from UnitLoadoutManager at runtime.
        /// </summary>
        private void SpawnPlayerUnit(GameObject prefab, CharacterData data, Tile tile)
        {
            if (prefab == null)
            {
                Debug.LogError($"[DebugMapSpawner] '{data.characterName}' has no prefab assigned — set CharacterData.prefab in the Inspector.");
                return;
            }

            if (tile == null)
            {
                Debug.LogError($"[DebugMapSpawner] Spawn tile for '{data.characterName}' is null — assign it in the Inspector.");
                return;
            }

            // Instantiate inactive so Unit.Awake() doesn't fire before characterData is assigned.
            prefab.SetActive(false);
            var go = Instantiate(prefab, tile.transform.position + spawnOffset, prefab.transform.rotation);
            prefab.SetActive(true);

            go.name = data.characterName;

            var unit = go.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"[DebugMapSpawner] Player prefab '{prefab.name}' has no Unit component.");
                Destroy(go);
                return;
            }

            unit.characterData = data;
            go.SetActive(true);
        }

        /// <summary>
        /// Spawns an enemy unit at the given tile.
        /// The EnemyLoadout component on the prefab already carries abilities and passive —
        /// no data injection required.
        /// </summary>
        private void SpawnEnemyUnit(GameObject prefab, Tile tile)
        {
            if (prefab == null)
            {
                Debug.LogError("[DebugMapSpawner] Enemy prefab is null.");
                return;
            }

            if (tile == null)
            {
                Debug.LogError($"[DebugMapSpawner] Spawn tile for enemy prefab '{prefab.name}' is null — assign it in the Inspector.");
                return;
            }

            if (prefab.GetComponent<EnemyLoadout>() == null)
                Debug.LogWarning($"[DebugMapSpawner] Enemy prefab '{prefab.name}' has no EnemyLoadout component. " +
                                 $"The unit will have no abilities or passive.");

            var go = Instantiate(prefab, tile.transform.position + spawnOffset, prefab.transform.rotation);
            go.name = go.name.Replace("(Clone)", "").Trim() + " (Enemy)";
        }
    }
}