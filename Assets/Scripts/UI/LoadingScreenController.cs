using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class LoadingScreenController : MonoBehaviour
    {
        public static LoadingScreenController Instance { get; private set; }

        [Header("Loading Screen")]
        [SerializeField] private GameObject screenRoot;
        [SerializeField] private Image progressFillImage;
        [SerializeField] private float holdAtFullDuration = 0.35f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (screenRoot == null)
                Debug.LogError("[LoadingScreenController] screenRoot is not assigned.");

            if (progressFillImage == null)
                Debug.LogError("[LoadingScreenController] progressFillImage is not assigned.");
        }

        public void Show()
        {
            if (screenRoot == null) return;
            screenRoot.SetActive(true);
            SetProgress(0f);
        }

        public void Hide()
        {
            if (screenRoot == null) return;
            screenRoot.SetActive(false);
        }

        public void SetProgress(float normalisedProgress)
        {
            if (progressFillImage == null) return;
            progressFillImage.fillAmount = Mathf.Clamp01(normalisedProgress);
        }

        public void LoadScene(string sceneName)
        {
            StartCoroutine(LoadSceneRoutine(sceneName));
        }

        private IEnumerator LoadSceneRoutine(string sceneName)
        {
            Show();

            var sceneLoad = SceneManager.LoadSceneAsync(sceneName);

            while (sceneLoad != null && !sceneLoad.isDone)
            {
                SetProgress(sceneLoad.progress);
                yield return null;
            }
        }

        public IEnumerator TrackHandles<T>(IList<AsyncOperationHandle<T>> handles)
        {
            Show();

            if (handles == null || handles.Count == 0)
            {
                SetProgress(1f);
                yield return new WaitForSeconds(holdAtFullDuration);
                Hide();
                yield break;
            }

            int total = handles.Count;

            while (true)
            {
                int completed = 0;
                for (int i = 0; i < total; i++)
                {
                    if (handles[i].IsDone)
                        completed++;
                }

                SetProgress((float)completed / total);

                if (completed >= total)
                    break;

                yield return null;
            }

            yield return new WaitForSeconds(holdAtFullDuration);
            Hide();
        }
    }
}