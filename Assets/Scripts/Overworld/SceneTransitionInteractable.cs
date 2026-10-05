using UnityEngine;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    public class SceneTransitionInteractable : OverworldInteractable
    {
        [Header("Scene Transition")]
        [SerializeField] private string destinationSceneName = "Debug";

        private bool _transitionTriggered;

        protected override bool CanInteract =>
            !_transitionTriggered && !string.IsNullOrEmpty(destinationSceneName);

        protected override void OnInteract()
        {
            _transitionTriggered = true;

            if (LoadingScreenController.Instance != null)
                LoadingScreenController.Instance.LoadScene(destinationSceneName);
            else
                SceneManager.LoadScene(destinationSceneName);
        }
    }
}