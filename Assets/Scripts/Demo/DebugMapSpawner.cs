using UnityEngine;
using UnityEngine.SceneManagement;

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
    /// Drag Tile scene objects into the player/enemy spawn tile arrays to define
    /// the valid spawn zone for each side. Each unit picks a random unoccupied
    /// tile from its pool — no two units will ever share a tile.
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
        
        [Header("Debug Navigation")]
        [Tooltip("Exact name of the debug lobby scene as it appears in Build Settings.")]
        [SerializeField] private string debugLobbySceneName = "DemoLobby";

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
                var config = playerSpawns[i];
                if (config.characterData == null)
                {
                    Debug.LogWarning($"[DebugMapSpawner] Player spawn {i} has no CharacterData — skipped.");
                    continue;
                }

                var tile = PickRandomTile(playerSpawnTiles, config.characterData.characterName);
                if (tile == null) break;

                SpawnPlayerUnit(config.characterData.prefab, config.characterData, tile);
            }

            // Enemy units — each carries its own prefab reference from the lobby
            var enemySpawns = DebugSessionConfig.EnemySpawns;
            for (int i = 0; i < enemySpawns.Count; i++)
            {
                var config = enemySpawns[i];
                if (config.prefab == null)
                {
                    Debug.LogWarning($"[DebugMapSpawner] Enemy spawn {i} has no prefab — skipped.");
                    continue;
                }

                var tile = PickRandomTile(enemySpawnTiles, config.prefab.name);
                if (tile == null) break;

                SpawnEnemyUnit(config.prefab, tile);
            }
        }

        // ── Fallback spawn ────────────────────────────────────────────────────

        private void SpawnFallback()
        {
            Debug.Log("[DebugMapSpawner] No lobby config found — using fallback spawn.");

            if (fallbackPlayerCharacters != null)
            {
                foreach (var cd in fallbackPlayerCharacters)
                {
                    if (cd == null) continue;

                    // Register a default loadout for fallback players so in-combat systems
                    // don't warn about missing loadout entries.
                    if (UnitLoadoutManager.Instance != null &&
                        !UnitLoadoutManager.Instance.HasPlayerLoadout(cd))
                    {
                        var pool = cd.availableAbilities;
                        var fallbackAbilities = new Ability[3];
                        if (pool != null)
                            for (int s = 0; s < 3 && s < pool.Length; s++)
                                fallbackAbilities[s] = pool[s];

                        UnitLoadoutManager.Instance.SetPlayerLoadout(cd, fallbackAbilities, passive: null);
                    }

                    var tile = PickRandomTile(playerSpawnTiles, cd.characterName);
                    if (tile == null) break;

                    SpawnPlayerUnit(cd.prefab, cd, tile);
                }
            }

            if (fallbackEnemyPrefabs != null)
            {
                foreach (var prefab in fallbackEnemyPrefabs)
                {
                    if (prefab == null) continue;

                    var tile = PickRandomTile(enemySpawnTiles, prefab.name);
                    if (tile == null) break;

                    SpawnEnemyUnit(prefab, tile);
                }
            }
        }

        // ── Tile selection ────────────────────────────────────────────────────

        /// <summary>
        /// Returns a random unoccupied tile from the given pool.
        /// Because each SpawnPlayerUnit/SpawnEnemyUnit call activates the unit and triggers
        /// Unit.Awake() (which calls SetCurrentTile), the tile's occupied flag is set
        /// immediately — so subsequent calls to this method will never return the same tile.
        /// Returns null and logs a warning if all tiles in the pool are taken.
        /// </summary>
        private Tile PickRandomTile(Tile[] pool, string unitName)
        {
            if (pool == null || pool.Length == 0)
            {
                Debug.LogWarning($"[DebugMapSpawner] Spawn tile pool is empty — cannot place '{unitName}'. Add tiles in the Inspector.");
                return null;
            }

            // Collect all unoccupied tiles from the pool.
            // Using a local list avoids modifying the serialised array.
            var available = new System.Collections.Generic.List<Tile>(pool.Length);
            foreach (var tile in pool)
            {
                if (tile != null && !tile.occupied)
                    available.Add(tile);
            }

            if (available.Count == 0)
            {
                Debug.LogWarning($"[DebugMapSpawner] All spawn tiles are occupied — cannot place '{unitName}'. Add more tiles in the Inspector.");
                return null;
            }

            return available[Random.Range(0, available.Count)];
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
        
        public void ReturnToLobby()
        {
            DebugSessionConfig.Clear();
            SceneManager.LoadScene(debugLobbySceneName);
        }
    }
}