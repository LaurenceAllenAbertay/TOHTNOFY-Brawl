using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [System.Serializable]
    public class MusicLayer
    {
        [Header("Layer Info")]
        public string layerName;
        public AudioClip audioClip;

        [Header("Playback Settings")]
        [Range(0f, 1f)] public float volume = 1f;
        public bool looping = true;
        public bool startEnabled = true;

        [Header("Fade Settings")]
        public float fadeInDuration = 1f;
        public float fadeOutDuration = 1f;

        [Header("Trigger Conditions")]
        public List<MusicTrigger> enableTriggers = new List<MusicTrigger>();
        public List<MusicTrigger> disableTriggers = new List<MusicTrigger>();

        // Runtime data
        [System.NonSerialized] public AudioSource audioSource;
        [System.NonSerialized] public bool isEnabled;
        [System.NonSerialized] public Coroutine fadeCoroutine;
    }
}