using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    public class OverworldInteractable : MonoBehaviour
    {
        [Header("Scene Transition")]
        [SerializeField] private string destinationSceneName = "Debug";

        [Header("UI")]
        [SerializeField] private GameObject interactPromptUI;
        
        private bool _playerInRange;
        
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
        
        private void HandleInteract()
        {
            if (!_playerInRange) return;

            if (string.IsNullOrEmpty(destinationSceneName))
            {
                return;
            }

            if (LoadingScreenController.Instance != null)
                LoadingScreenController.Instance.LoadScene(destinationSceneName);
            else
                SceneManager.LoadScene(destinationSceneName);
        }
        
        private void ShowPrompt() => interactPromptUI?.SetActive(true);
        private void HidePrompt() => interactPromptUI?.SetActive(false);
    }
}