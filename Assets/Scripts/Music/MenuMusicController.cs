using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(AudioSource))]
    public class MenuMusicController : MonoBehaviour
    {
        public static MenuMusicController Instance { get; private set; }

        [SerializeField] private AudioSource audioSource;
        [SerializeField] private float fadeOutDuration = 0.75f;

        private Coroutine fadeRoutine;
        private float baseVolume;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            baseVolume = audioSource.volume;
        }

        private void Start()
        {
            if (!audioSource.isPlaying)
                audioSource.Play();
        }

        public void StopMusic()
        {
            if (!audioSource.isPlaying) return;

            if (fadeRoutine != null)
                StopCoroutine(fadeRoutine);

            fadeRoutine = StartCoroutine(FadeOutAndStop());
        }

        private IEnumerator FadeOutAndStop()
        {
            float startVolume = audioSource.volume;
            float elapsed = 0f;

            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                audioSource.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            audioSource.Stop();
            audioSource.volume = baseVolume;
            fadeRoutine = null;

            Instance = null;
            Destroy(gameObject);
        }
    }
}