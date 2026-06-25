using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Spawns the player party into the DebugOverworld scene by reading
    /// DebugSessionConfig.PlayerSpawns, which was written by DebugLobbyController
    /// in selection-queue order (index 0 = leader).
    ///
    /// ── What this spawner does ────────────────────────────────────────────────
    /// • Instantiates each character's CharacterData.overworldPrefab.
    /// • Adds OverworldPartyLeader (marker) to the leader's GameObject so
    ///   OverworldInteractable can detect it via trigger collisions.
    /// • Passes all instantiated GameObjects to OverworldPartyManager.Initialise()
    ///   in leader-first order — the manager owns all movement from that point.
    /// • Calls OverworldCameraController.SetTarget with the leader's Transform.
    ///
    /// ── Ability data ─────────────────────────────────────────────────────────
    /// No extra work needed here. UnitLoadoutManager uses DontDestroyOnLoad and
    /// DebugSessionConfig is a static class — both survive the overworld scene and
    /// are intact when DebugMapSpawner reads them in the combat scene.
    ///
    /// ── Scene Setup ──────────────────────────────────────────────────────────
    /// 1. Add this component to the scene manager GameObject alongside
    ///    OverworldPartyManager (they can share a single empty GameObject).
    /// 2. Assign the Spawn Point transform, the OverworldCameraController,
    ///    and the OverworldPartyManager reference in the Inspector.
    /// 3. Optionally assign Fallback Characters for running standalone.
    ///
    /// ── Execution Order ──────────────────────────────────────────────────────
    /// Project Settings → Script Execution Order:
    ///   DebugOverworldSpawner → -100  (before OverworldCameraController's Start)
    /// </summary>
    public class DebugOverworldSpawner : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Party Manager")]
        [Tooltip("The OverworldPartyManager that will own all party movement. " +
                 "Place on the same manager GameObject as this spawner.")]
        [SerializeField] private OverworldPartyManager partyManager;

        [Header("Spawn Point")]
        [Tooltip("World position where the party leader spawns. " +
                 "Followers stagger behind this point along the -Z axis.")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("World-space gap between each unit's initial spawn position. " +
                 "OverworldPartyManager will close any gap naturally over the first few frames.")]
        [SerializeField] private float spawnSpacing = 1.0f;

        [Header("Camera")]
        [Tooltip("The OverworldCameraController in this scene. " +
                 "Its target is set to the spawned leader's Transform automatically.")]
        [SerializeField] private OverworldCameraController cameraController;

        [SerializeField] private string debugLobbySceneName;
        
        [Header("Fallback (run DebugOverworld without lobby)")]
        [Tooltip("Characters to spawn when no DebugSessionConfig data is present. " +
                 "Index 0 = leader. Must have overworldPrefab assigned on CharacterData.")]
        [SerializeField] private CharacterData[] fallbackCharacters;

        // ── Lifecycle ─────────────────────────────────────────────────────────

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

        // ── Spawn from lobby config ───────────────────────────────────────────

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

        // ── Fallback spawn ────────────────────────────────────────────────────

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

                // Register a minimal loadout so combat systems don't warn if the
                // player enters combat from this standalone overworld session.
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

        // ── Party spawning ────────────────────────────────────────────────────

        /// <summary>
        /// Instantiates each character's overworldPrefab in leader-first order,
        /// attaches the OverworldPartyLeader marker to the first unit, then
        /// hands all GameObjects to OverworldPartyManager to own from here on.
        /// </summary>
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

                // Stagger spawn positions along -Z so units don't overlap on the first frame.
                Vector3 spawnPos = origin + new Vector3(0f, 0f, -i * spawnSpacing);
                var go = Instantiate(cd.overworldPrefab, spawnPos,
                                     cd.overworldPrefab.transform.rotation);
                go.name = cd.characterName;

                // Mark the leader so OverworldInteractable can identify it via trigger.
                if (i == 0)
                    go.AddComponent<OverworldPartyLeader>();

                partyObjects.Add(go);
            }

            // Hand the full party to the manager — it owns movement from this point.
            partyManager.Initialise(partyObjects);

            // Aim the camera at the leader.
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