using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    public class DebugMapSpawner : MonoBehaviour
    {
        [Header("Spawn Offset")]
        [SerializeField] private Vector3 spawnOffset = new Vector3(0f, 1.5f, 0f);

        [Header("Spawn Tiles")]
        [SerializeField] private Tile[] playerSpawnTiles;

        [SerializeField] private Tile[] enemySpawnTiles;

        [Header("Fallback (for running Debug scene without lobby)")]
        [SerializeField] private CharacterData[] fallbackPlayerCharacters;
        
        [SerializeField] private GameObject[] fallbackEnemyPrefabs;
        
        [Header("Debug Navigation")]
        [SerializeField] private string debugLobbySceneName = "DemoLobby";
        
        private void Awake()
        {
            bool hasLobbyData = DebugSessionConfig.PlayerSpawns.Count > 0
                             || DebugSessionConfig.EnemySpawns.Count  > 0;

            if (hasLobbyData)
                SpawnFromConfig();
            else
                SpawnFallback();
        }
        
        private void SpawnFromConfig()
        {
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
        
        private void SpawnFallback()
        {
            Debug.Log("[DebugMapSpawner] No lobby config found — using fallback spawn.");

            if (fallbackPlayerCharacters != null)
            {
                foreach (var cd in fallbackPlayerCharacters)
                {
                    if (cd == null) continue;
                    
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
        
        private Tile PickRandomTile(Tile[] pool, string unitName)
        {
            if (pool == null || pool.Length == 0)
            {
                Debug.LogWarning($"[DebugMapSpawner] Spawn tile pool is empty — cannot place '{unitName}'. Add tiles in the Inspector.");
                return null;
            }
            
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