using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
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
        
        [SerializeField] private CharacterData[] fallbackEnemyCharacters;
        
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

                StartCoroutine(SpawnPlayerUnit(config.characterData, tile));
            }
            
            var enemySpawns = DebugSessionConfig.EnemySpawns;
            for (int i = 0; i < enemySpawns.Count; i++)
            {
                var config = enemySpawns[i];
                if (config.characterData == null)
                {
                    Debug.LogWarning($"[DebugMapSpawner] Enemy spawn {i} has no CharacterData — skipped.");
                    continue;
                }

                var tile = PickRandomTile(enemySpawnTiles, config.characterData.characterName);
                if (tile == null) break;

                StartCoroutine(SpawnEnemyUnit(config.characterData, tile));
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

                    var tile = PickRandomTile(playerSpawnTiles, cd.characterName);
                    if (tile == null) break;

                    StartCoroutine(SpawnPlayerUnit(cd, tile));
                }
            }

            if (fallbackEnemyCharacters != null)
            {
                foreach (var cd in fallbackEnemyCharacters)
                {
                    if (cd == null) continue;

                    var tile = PickRandomTile(enemySpawnTiles, cd.characterName);
                    if (tile == null) break;

                    StartCoroutine(SpawnEnemyUnit(cd, tile));
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

        private IEnumerator SpawnPlayerUnit(CharacterData data, Tile tile)
        {
            if (data.prefab == null || !data.prefab.RuntimeKeyIsValid())
            {
                Debug.LogError($"[DebugMapSpawner] '{data.characterName}' has no prefab assigned — set CharacterData.prefab in the Inspector.");
                yield break;
            }

            if (tile == null)
            {
                Debug.LogError($"[DebugMapSpawner] Spawn tile for '{data.characterName}' is null — assign it in the Inspector.");
                yield break;
            }

            AsyncOperationHandle<GameObject> handle = data.prefab.InstantiateAsync();
            yield return handle;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[DebugMapSpawner] Failed to load prefab for '{data.characterName}'.");
                yield break;
            }

            var go = handle.Result;
            go.transform.position = tile.transform.position + spawnOffset;
            go.name = data.characterName;

            var unit = go.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"[DebugMapSpawner] Player prefab '{go.name}' has no Unit component.");
                Addressables.ReleaseInstance(go);
                yield break;
            }

            unit.ApplyCharacterData(data);
        }
        
        private IEnumerator SpawnEnemyUnit(CharacterData data, Tile tile)
        {
            if (data.prefab == null || !data.prefab.RuntimeKeyIsValid())
            {
                Debug.LogError($"[DebugMapSpawner] '{data.characterName}' has no prefab assigned — set CharacterData.prefab in the Inspector.");
                yield break;
            }

            if (tile == null)
            {
                Debug.LogError($"[DebugMapSpawner] Spawn tile for enemy '{data.characterName}' is null — assign it in the Inspector.");
                yield break;
            }

            AsyncOperationHandle<GameObject> handle = data.prefab.InstantiateAsync();
            yield return handle;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[DebugMapSpawner] Failed to load prefab for enemy '{data.characterName}'.");
                yield break;
            }

            var go = handle.Result;
            go.transform.position = tile.transform.position + spawnOffset;
            go.name = data.characterName + " (Enemy)";

            var unit = go.GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"[DebugMapSpawner] Enemy prefab '{go.name}' has no Unit component.");
                Addressables.ReleaseInstance(go);
                yield break;
            }

            unit.ApplyCharacterData(data);
        }
        
        public void ReturnToLobby()
        {
            DebugSessionConfig.Clear();
            SceneManager.LoadScene(debugLobbySceneName);
        }
    }
}