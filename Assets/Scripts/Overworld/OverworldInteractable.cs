using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class OverworldInteractable : MonoBehaviour
    {
        [Header("UI")]
        [SerializeField] private GameObject interactPromptUI;

        private bool _playerInRange;

        protected bool PlayerInRange => _playerInRange;

        protected virtual bool CanInteract => true;

        protected virtual void Awake()
        {
            HidePrompt();
        }

        protected virtual void OnEnable()
        {
            OverworldInputHandler.OnInteractPressed += HandleInteract;
            OverworldDialogueManager.OnConversationEnded += RefreshPrompt;
        }

        protected virtual void OnDisable()
        {
            OverworldInputHandler.OnInteractPressed -= HandleInteract;
            OverworldDialogueManager.OnConversationEnded -= RefreshPrompt;

            _playerInRange = false;
            HidePrompt();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponent<OverworldPartyLeader>() == null) return;

            _playerInRange = true;
            RefreshPrompt();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponent<OverworldPartyLeader>() == null) return;

            _playerInRange = false;
            HidePrompt();
        }

        private void HandleInteract()
        {
            if (!_playerInRange) return;
            if (OverworldDialogueManager.IsPlaying) return;
            if (!CanInteract) return;

            HidePrompt();
            OnInteract();
        }

        protected abstract void OnInteract();

        protected void RefreshPrompt()
        {
            if (_playerInRange && CanInteract && !OverworldDialogueManager.IsPlaying)
                ShowPrompt();
            else
                HidePrompt();
        }

        private void ShowPrompt() => interactPromptUI?.SetActive(true);

        private void HidePrompt() => interactPromptUI?.SetActive(false);
    }
}