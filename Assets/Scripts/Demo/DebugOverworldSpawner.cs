using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    public class DebugOverworldSpawner : MonoBehaviour
    {
        [Header("Party Manager")]
        [SerializeField] private OverworldPartyManager partyManager;

        [Header("Spawn Point")]
        [SerializeField] private Transform spawnPoint;

        [SerializeField] private float spawnSpacing = 1.0f;

        [Header("Camera")]
        [SerializeField] private OverworldCameraController cameraController;

        [SerializeField] private string debugLobbySceneName;
        
        [Header("Fallback (run DebugOverworld without lobby)")]
        [SerializeField] private CharacterData[] fallbackCharacters;
        
        private void Awake()
        {
            if (partyManager == null)
            {
                Debug.LogError("[DebugOverworldSpawner] partyManager is not assigned. " +
                               "Assign OverworldPartyManager in the Inspector.");
                return;
            }

            if (DebugSessionConfig.PlayerSpawns.Count > 0)
                SpawnFromConfig();
            else
                SpawnFallback();
        }
        
        private void SpawnFromConfig()
        {
            var characters = new List<CharacterData>();

            foreach (var spawn in DebugSessionConfig.PlayerSpawns)
            {
                if (spawn?.characterData == null)
                {
                    Debug.LogWarning("[DebugOverworldSpawner] PlayerSpawn has null CharacterData — skipped.");
                    continue;
                }

                if (spawn.characterData.overworldPrefab == null || !spawn.characterData.overworldPrefab.RuntimeKeyIsValid())
                {
                    Debug.LogWarning($"[DebugOverworldSpawner] '{spawn.characterData.characterName}' " +
                                     $"has no overworldPrefab assigned on CharacterData — skipped.");
                    continue;
                }

                characters.Add(spawn.characterData);
            }

            StartCoroutine(SpawnParty(characters));
        }
        
        private void SpawnFallback()
        {
            Debug.Log("[DebugOverworldSpawner] No lobby config found — using fallback characters.");

            if (fallbackCharacters == null || fallbackCharacters.Length == 0)
            {
                Debug.LogWarning("[DebugOverworldSpawner] Fallback characters list is empty. " +
                                 "Assign characters in the Inspector to run this scene standalone.");
                return;
            }

            var characters = new List<CharacterData>();
            foreach (var cd in fallbackCharacters)
            {
                if (cd == null) continue;

                if (cd.overworldPrefab == null || !cd.overworldPrefab.RuntimeKeyIsValid())
                {
                    Debug.LogWarning($"[DebugOverworldSpawner] Fallback '{cd.characterName}' " +
                                     $"has no overworldPrefab — skipped.");
                    continue;
                }

                characters.Add(cd);
            }

            StartCoroutine(SpawnParty(characters));
        }
        
        private IEnumerator SpawnParty(List<CharacterData> characters)
        {
            if (characters.Count == 0)
            {
                Debug.LogWarning("[DebugOverworldSpawner] No valid characters to spawn.");
                yield break;
            }

            Vector3 origin = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            var     partyObjects = new List<GameObject>(characters.Count);

            for (int i = 0; i < characters.Count; i++)
            {
                var cd = characters[i];

                AsyncOperationHandle<GameObject> handle = cd.overworldPrefab.InstantiateAsync();
                yield return handle;

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"[DebugOverworldSpawner] Failed to load overworldPrefab for '{cd.characterName}'.");
                    continue;
                }

                var go = handle.Result;
                go.transform.position = origin + new Vector3(0f, 0f, -partyObjects.Count * spawnSpacing);
                go.name = cd.characterName;

                if (partyObjects.Count == 0)
                    go.AddComponent<OverworldPartyLeader>();

                partyObjects.Add(go);
            }

            if (partyObjects.Count == 0)
            {
                Debug.LogWarning("[DebugOverworldSpawner] No valid characters to spawn.");
                yield break;
            }

            partyManager.Initialise(partyObjects);
            
            if (cameraController != null)
                cameraController.SetTarget(partyManager.LeaderTransform);
            else
                Debug.LogWarning("[DebugOverworldSpawner] cameraController is not assigned — " +
                                 "camera will not follow the leader.");
        }
        
        public void ReturnToLobby()
        {
            DebugSessionConfig.Clear();
            SceneManager.LoadScene(debugLobbySceneName);
        }
    }
}