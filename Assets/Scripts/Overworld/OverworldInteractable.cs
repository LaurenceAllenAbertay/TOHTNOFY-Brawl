using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Marks a world-space object as interactable during the overworld phase.
    /// When the party leader enters the trigger zone and presses the Interact button,
    /// the specified destination scene is loaded.
    ///
    /// ── GameObject Setup ─────────────────────────────────────────────────────
    /// 1. Attach this component to the interactable GameObject.
    /// 2. Add a Collider (e.g. BoxCollider) to this GameObject and tick "Is Trigger".
    ///    Adjust the size to set the interaction zone.
    /// 3. The leader's GameObject needs:
    ///      • A non-trigger BoxCollider so Unity's physics engine can detect the overlap.
    ///      • A Kinematic Rigidbody — required for OnTriggerEnter/Exit to fire when
    ///        the player moves via transform (not physics forces).
    ///      • An OverworldPartyLeader component — added automatically by
    ///        DebugOverworldSpawner on the first (leader) prefab.
    /// 4. Assign destinationSceneName in the Inspector and ensure that scene is
    ///    added to File → Build Settings.
    ///
    /// ── Interaction Flow ─────────────────────────────────────────────────────
    /// 1. Leader walks into the trigger zone → _playerInRange = true.
    /// 2. Player presses Interact → OverworldInputHandler.OnInteractPressed fires.
    /// 3. If _playerInRange → SceneManager.LoadScene(destinationSceneName).
    ///
    /// ── Input Isolation ──────────────────────────────────────────────────────
    /// OverworldInputHandler.OnDisable re-enables the Gameplay action map as the
    /// destination scene loads, so combat input is active the moment the combat
    /// scene's systems initialise. No extra wiring needed here.
    /// </summary>
    public class OverworldInteractable : MonoBehaviour
    {
        [Header("Scene Transition")]
        [Tooltip("Exact name of the scene to load on interaction, as it appears in Build Settings.")]
        [SerializeField] private string destinationSceneName = "Debug";

        [Header("UI")]
        [Tooltip("GameObject shown while the leader is inside the interaction zone. " +
                 "Assign any world-space or screen-space prompt here — it is hidden on start.")]
        [SerializeField] private GameObject interactPromptUI;

        // ── Private ───────────────────────────────────────────────────────────

        private bool _playerInRange;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            HidePrompt();
        }

        private void OnEnable()
        {
            OverworldInputHandler.OnInteractPressed += HandleInteract;
        }

        private void OnDisable()
        {
            OverworldInputHandler.OnInteractPressed -= HandleInteract;
            _playerInRange = false;
            HidePrompt();
        }

        // ── Proximity detection ───────────────────────────────────────────────

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<OverworldPartyLeader>() != null)
            {
                _playerInRange = true;
                ShowPrompt();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponent<OverworldPartyLeader>() != null)
            {
                _playerInRange = false;
                HidePrompt();
            }
        }

        // ── Interaction ───────────────────────────────────────────────────────

        private void HandleInteract()
        {
            if (!_playerInRange) return;

            if (string.IsNullOrEmpty(destinationSceneName))
            {
                Debug.LogError("[OverworldInteractable] destinationSceneName is not set — " +
                               "assign the combat scene name in the Inspector.");
                return;
            }

            SceneManager.LoadScene(destinationSceneName);
        }

        // ── UI helpers ────────────────────────────────────────────────────────

        private void ShowPrompt() => interactPromptUI?.SetActive(true);
        private void HidePrompt() => interactPromptUI?.SetActive(false);
    }
}