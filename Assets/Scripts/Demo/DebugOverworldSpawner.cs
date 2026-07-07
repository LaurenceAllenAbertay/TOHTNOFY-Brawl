using System.Collections.Generic;
using UnityEngine;
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

                if (spawn.characterData.overworldPrefab == null)
                {
                    Debug.LogWarning($"[DebugOverworldSpawner] '{spawn.characterData.characterName}' " +
                                     $"has no overworldPrefab assigned on CharacterData — skipped.");
                    continue;
                }

                characters.Add(spawn.characterData);
            }

            SpawnParty(characters);
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

                if (cd.overworldPrefab == null)
                {
                    Debug.LogWarning($"[DebugOverworldSpawner] Fallback '{cd.characterName}' " +
                                     $"has no overworldPrefab — skipped.");
                    continue;
                }
                
                if (UnitLoadoutManager.Instance != null &&
                    !UnitLoadoutManager.Instance.HasPlayerLoadout(cd))
                {
                    var pool             = cd.availableAbilities;
                    var fallbackAbilities = new Ability[3];
                    if (pool != null)
                        for (int s = 0; s < 3 && s < pool.Length; s++)
                            fallbackAbilities[s] = pool[s];

                    UnitLoadoutManager.Instance.SetPlayerLoadout(cd, fallbackAbilities, passive: null);
                }

                characters.Add(cd);
            }

            SpawnParty(characters);
        }
        
        private void SpawnParty(List<CharacterData> characters)
        {
            if (characters.Count == 0)
            {
                Debug.LogWarning("[DebugOverworldSpawner] No valid characters to spawn.");
                return;
            }

            Vector3          origin      = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            var              partyObjects = new List<GameObject>(characters.Count);

            for (int i = 0; i < characters.Count; i++)
            {
                var cd = characters[i];
                
                Vector3 spawnPos = origin + new Vector3(0f, 0f, -i * spawnSpacing);
                var go = Instantiate(cd.overworldPrefab, spawnPos,
                                     cd.overworldPrefab.transform.rotation);
                go.name = cd.characterName;

                if (i == 0)
                    go.AddComponent<OverworldPartyLeader>();

                partyObjects.Add(go);
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