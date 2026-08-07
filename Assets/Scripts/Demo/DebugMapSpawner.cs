using System.Collections;
using System.Collections.Generic;
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

        private readonly HashSet<Tile> _claimedThisBatch = new HashSet<Tile>();

        private struct SpawnRequest
        {
            public CharacterData data;
            public Tile tile;
            public bool isEnemy;
        }
        
        private void Awake()
        {
            bool hasLobbyData = DebugSessionConfig.PlayerSpawns.Count > 0
                             || DebugSessionConfig.EnemySpawns.Count  > 0;

            _claimedThisBatch.Clear();

            var requests = hasLobbyData ? BuildRequestsFromConfig() : BuildFallbackRequests();

            StartCoroutine(SpawnAll(requests));
        }
        
        private List<SpawnRequest> BuildRequestsFromConfig()
        {
            var requests = new List<SpawnRequest>();

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

                requests.Add(new SpawnRequest { data = config.characterData, tile = tile, isEnemy = false });
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

                requests.Add(new SpawnRequest { data = config.characterData, tile = tile, isEnemy = true });
            }

            return requests;
        }
        
        private List<SpawnRequest> BuildFallbackRequests()
        {
            Debug.Log("[DebugMapSpawner] No lobby config found — using fallback spawn.");

            var requests = new List<SpawnRequest>();

            if (fallbackPlayerCharacters != null)
            {
                foreach (var cd in fallbackPlayerCharacters)
                {
                    if (cd == null) continue;

                    var tile = PickRandomTile(playerSpawnTiles, cd.characterName);
                    if (tile == null) break;

                    requests.Add(new SpawnRequest { data = cd, tile = tile, isEnemy = false });
                }
            }

            if (fallbackEnemyCharacters != null)
            {
                foreach (var cd in fallbackEnemyCharacters)
                {
                    if (cd == null) continue;

                    var tile = PickRandomTile(enemySpawnTiles, cd.characterName);
                    if (tile == null) break;

                    requests.Add(new SpawnRequest { data = cd, tile = tile, isEnemy = true });
                }
            }

            return requests;
        }
        
        private Tile PickRandomTile(Tile[] pool, string unitName)
        {
            if (pool == null || pool.Length == 0)
            {
                Debug.LogWarning($"[DebugMapSpawner] Spawn tile pool is empty — cannot place '{unitName}'. Add tiles in the Inspector.");
                return null;
            }
            
            var available = new List<Tile>(pool.Length);
            foreach (var tile in pool)
            {
                if (tile != null && !tile.occupied && !_claimedThisBatch.Contains(tile))
                    available.Add(tile);
            }

            if (available.Count == 0)
            {
                Debug.LogWarning($"[DebugMapSpawner] All spawn tiles are occupied — cannot place '{unitName}'. Add more tiles in the Inspector.");
                return null;
            }

            var picked = available[Random.Range(0, available.Count)];
            _claimedThisBatch.Add(picked);
            return picked;
        }

        private IEnumerator SpawnAll(List<SpawnRequest> requests)
        {
            var handles  = new List<AsyncOperationHandle<GameObject>>(requests.Count);
            var launched = new List<SpawnRequest>(requests.Count);

            foreach (var request in requests)
            {
                if (request.data.prefab == null || !request.data.prefab.RuntimeKeyIsValid())
                {
                    Debug.LogError($"[DebugMapSpawner] '{request.data.characterName}' has no prefab assigned — set CharacterData.prefab in the Inspector.");
                    continue;
                }

                handles.Add(request.data.prefab.InstantiateAsync(
                    request.tile.transform.position + spawnOffset, Quaternion.identity));
                launched.Add(request);
            }

            for (int i = 0; i < handles.Count; i++)
            {
                if (!handles[i].IsDone)
                    yield return handles[i];
            }

            for (int i = 0; i < handles.Count; i++)
            {
                var request = launched[i];

                if (handles[i].Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"[DebugMapSpawner] Failed to load prefab for '{request.data.characterName}'.");
                    continue;
                }

                var go = handles[i].Result;
                go.name = request.isEnemy
                    ? request.data.characterName + " (Enemy)"
                    : request.data.characterName;

                var unit = go.GetComponent<Unit>();
                if (unit == null)
                {
                    Debug.LogError($"[DebugMapSpawner] Prefab '{go.name}' has no Unit component.");
                    Addressables.ReleaseInstance(go);
                    continue;
                }

                unit.ApplyCharacterData(request.data);
                unit.SetCurrentTile(request.tile);
                UnitManager.RegisterUnit(unit);
            }

            if (TurnManager.Instance == null)
            {
                Debug.LogError("[DebugMapSpawner] TurnManager not found in scene — combat cannot start.");
                yield break;
            }

            TurnManager.Instance.BeginCombat();
        }
        
        public void ReturnToLobby()
        {
            DebugSessionConfig.Clear();
            SceneManager.LoadScene(debugLobbySceneName);
        }
    }
}