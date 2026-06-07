using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class DynamicMusicManager : MonoBehaviour
    {
        public static DynamicMusicManager Instance { get; private set; }

        [Header("Current Track")]
        [SerializeField] private MusicTrack currentTrack;

        [Header("Settings")]
        [Range(0f, 1f)][SerializeField] private float globalMusicVolume = 0.8f;
        [SerializeField] private bool enableDynamicMusic = true;

        [Header("Object Pool")]
        [SerializeField] private int poolSize = 10;

        // Object Pool - pre-created AudioSource objects
        private Queue<AudioSource> audioSourcePool = new Queue<AudioSource>();
        private List<AudioSource> activeAudioSources = new List<AudioSource>();

        // Runtime data
        private Dictionary<string, MusicLayer> layerLookup = new Dictionary<string, MusicLayer>();
        private bool trackIsPlaying = false;

        // Game state tracking
        private int currentTurnNumber = 0;
        private int currentTotalTurnNumber = 0;
        private Unit lastActiveUnit;
        private CombatState lastCombatState = CombatState.WaitingForInput;

        // Events for custom triggers
        public static event System.Action<string> OnCustomMusicEvent;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Initialize object pool immediately
            InitializeObjectPool();

            if (enableDynamicMusic)
            {
                Initialize();
            }
        }

        private void InitializeObjectPool()
        {
            // Create pool of AudioSource objects as children
            for (int i = 0; i < poolSize; i++)
            {
                GameObject poolObj = new GameObject($"AudioSource_Pool_{i}");
                poolObj.transform.SetParent(transform);

                AudioSource source = poolObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.volume = 0f;
                source.priority = 64;

                poolObj.SetActive(false); // Inactive by default
                audioSourcePool.Enqueue(source);
            }
        }

        private void Initialize()
        {
            SubscribeToEvents();

            // Load initial track immediately if set
            if (currentTrack != null)
            {
                LoadTrack(currentTrack);
            }
        }

        private void Start()
        {
            // Start monitoring after everything else is initialized
            if (enableDynamicMusic)
            {
                StartCoroutine(MonitorGameState());
            }
        }

        private void SubscribeToEvents()
        {
            TurnManager.OnTurnStarted += OnTurnStarted;
            TurnManager.OnTurnEnded += OnTurnEnded;
            TurnManager.OnTurnNumberChanged += OnTurnNumberChanged;
            UnitManager.OnUnitDied += OnUnitDied;
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();

            if (Instance == this)
                Instance = null;
        }

        private void UnsubscribeFromEvents()
        {
            TurnManager.OnTurnStarted -= OnTurnStarted;
            TurnManager.OnTurnEnded -= OnTurnEnded;
            TurnManager.OnTurnNumberChanged -= OnTurnNumberChanged;
            UnitManager.OnUnitDied -= OnUnitDied;
        }

        #region Object Pool Management

        private AudioSource GetPooledAudioSource()
        {
            if (audioSourcePool.Count > 0)
            {
                AudioSource source = audioSourcePool.Dequeue();
                source.gameObject.SetActive(true);
                activeAudioSources.Add(source);
                return source;
            }

            Debug.LogWarning("AudioSource pool exhausted! Consider increasing pool size.");
            return null;
        }

        private void ReturnToPool(AudioSource source)
        {
            if (source == null) return;

            source.Stop();
            source.clip = null;
            source.volume = 0f;
            source.loop = false;
            source.gameObject.SetActive(false);

            activeAudioSources.Remove(source);
            audioSourcePool.Enqueue(source);
        }

        #endregion

        #region Event Handlers

        private void OnTurnStarted(Unit unit)
        {
            if (!enableDynamicMusic || unit == null) return;

            if (unit != lastActiveUnit)
            {
                ProcessUnitTypeTriggers(unit);
                lastActiveUnit = unit;
            }

            // Process turn number triggers immediately when turn starts
            var turnManager = FindAnyObjectByType<TurnManager>();
            if (turnManager != null)
            {
                int totalTurns = turnManager.TotalTurnCount;
                int currentTurn = turnManager.GetTurnInCurrentRound();

                // Process turn number triggers for the current turn
                ProcessTurnNumberTriggers(totalTurns, currentTurn);

                // Update our tracking variables
                currentTurnNumber = currentTurn;
                currentTotalTurnNumber = totalTurns;
            }

            // Process combat state triggers for turn start
            ProcessCombatStateTriggers(CombatState.WaitingForInput, unit);
        }

        private void OnTurnEnded(Unit unit)
        {
            if (!enableDynamicMusic) return;

            // Process combat state triggers for turn ending
            ProcessCombatStateTriggers(CombatState.TurnEnding, unit);
        }

        private void OnTurnNumberChanged(int totalTurnNumber, int turnNumber)
        {
            if (!enableDynamicMusic) return;

            if (turnNumber != currentTurnNumber)
            {
                ProcessTurnNumberTriggers(totalTurnNumber, turnNumber);
                currentTurnNumber = turnNumber;
                currentTotalTurnNumber = totalTurnNumber;
            }
        }

        private void OnUnitDied(Unit unit)
        {
            if (!enableDynamicMusic || unit == null) return;
            TriggerCustomEvent($"UnitDied_{unit.GetType().Name}");
        }

        #endregion

        #region Public API

        public void LoadTrack(MusicTrack track)
        {
            if (!enableDynamicMusic || track == null) return;

            // Clean up current track
            StopCurrentTrack();

            currentTrack = track;
            layerLookup.Clear();

            // Create audio sources for each layer using object pool
            foreach (var layer in track.layers)
            {
                CreateLayerAudioSource(layer);
                layerLookup[layer.layerName] = layer;
            }

            // Start ALL layers simultaneously for perfect sync
            StartAllLayersSynchronized();
        }

        public void StopCurrentTrack()
        {
            trackIsPlaying = false;

            // Return all active sources to pool
            for (int i = activeAudioSources.Count - 1; i >= 0; i--)
            {
                ReturnToPool(activeAudioSources[i]);
            }

            layerLookup.Clear();
        }

        public void EnableLayer(string layerName, float fadeTime = -1f)
        {
            if (!enableDynamicMusic || !layerLookup.TryGetValue(layerName, out MusicLayer layer))
                return;

            if (layer.isEnabled) return;

            float actualFadeTime = fadeTime >= 0 ? fadeTime : layer.fadeInDuration;

            // Don't start/stop playback - just fade volume
            layer.isEnabled = true;
            StartCoroutine(FadeLayerVolume(layer, 0f, GetTargetVolume(layer), actualFadeTime));
        }

        public void DisableLayer(string layerName, float fadeTime = -1f)
        {
            if (!enableDynamicMusic || !layerLookup.TryGetValue(layerName, out MusicLayer layer))
                return;

            if (!layer.isEnabled) return;

            float actualFadeTime = fadeTime >= 0 ? fadeTime : layer.fadeOutDuration;

            // Don't stop playback - just fade volume to 0
            layer.isEnabled = false;
            StartCoroutine(FadeLayerVolume(layer, layer.audioSource.volume, 0f, actualFadeTime));
        }

        public void TriggerCustomEvent(string eventName)
        {
            if (!enableDynamicMusic) return;

            OnCustomMusicEvent?.Invoke(eventName);
            ProcessCustomEventTriggers(eventName);
        }

        public void SetGlobalVolume(float volume)
        {
            globalMusicVolume = Mathf.Clamp01(volume);
            UpdateAllVolumes();
        }

        public void SetDynamicMusicEnabled(bool enabled)
        {
            enableDynamicMusic = enabled;

            if (!enabled)
            {
                StopCurrentTrack();
            }
        }

        #endregion

        #region Layer Management

        private void CreateLayerAudioSource(MusicLayer layer)
        {
            if (layer.audioClip == null) return;

            AudioSource source = GetPooledAudioSource();
            if (source == null) return;

            // Configure audio source
            source.clip = layer.audioClip;
            source.loop = layer.looping;
            source.volume = 0f; // Start muted
            source.playOnAwake = false;

            layer.audioSource = source;
            layer.isEnabled = false; // Will be set based on startEnabled when track starts
        }

        private void StartAllLayersSynchronized()
        {
            if (currentTrack == null) return;

            // Start ALL AudioSources at exactly the same time for perfect sync
            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null)
                {
                    layer.audioSource.Play();
                }
            }

            trackIsPlaying = true;

            // Now set initial layer states (enabled/disabled via volume, not playback)
            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null)
                {
                    layer.isEnabled = layer.startEnabled;

                    if (layer.startEnabled)
                    {
                        // Fade in enabled layers
                        StartCoroutine(FadeLayerVolume(layer, 0f, GetTargetVolume(layer), layer.fadeInDuration));
                    }
                    else
                    {
                        // Keep disabled layers muted but playing
                        layer.audioSource.volume = 0f;
                    }
                }
            }

            Debug.Log($"Started track '{currentTrack.trackName}' with {currentTrack.layers.Count} synchronized layers");
        }

        private IEnumerator FadeLayerVolume(MusicLayer layer, float fromVolume, float toVolume, float duration)
        {
            if (layer.audioSource == null) yield break;

            if (layer.fadeCoroutine != null)
                StopCoroutine(layer.fadeCoroutine);

            layer.fadeCoroutine = StartCoroutine(FadeAudioSourceVolume(layer.audioSource, fromVolume, toVolume, duration));
        }

        private IEnumerator FadeAudioSourceVolume(AudioSource source, float fromVolume, float toVolume, float duration)
        {
            if (duration <= 0f)
            {
                source.volume = toVolume;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                source.volume = Mathf.Lerp(fromVolume, toVolume, t);
                yield return null;
            }

            source.volume = toVolume;
        }

        private float GetTargetVolume(MusicLayer layer)
        {
            return layer.volume * (currentTrack?.masterVolume ?? 1f) * globalMusicVolume;
        }

        private void UpdateAllVolumes()
        {
            if (currentTrack == null) return;

            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null && layer.isEnabled)
                {
                    layer.audioSource.volume = GetTargetVolume(layer);
                }
            }
        }

        #endregion

        #region Simplified State Monitoring

        private IEnumerator MonitorGameState()
        {
            while (enableDynamicMusic)
            {
                yield return new WaitForSeconds(0.5f); // Check twice per second

                // Monitor combat state changes
                var combatManager = FindAnyObjectByType<CombatManager>();
                if (combatManager != null && combatManager.currentState != lastCombatState)
                {
                    ProcessCombatStateTriggers(combatManager.currentState, combatManager.CurrentActiveUnit);
                    lastCombatState = combatManager.currentState;
                }

                // Check health thresholds occasionally
                if (Time.frameCount % 120 == 0) // Every ~2 seconds at 60fps
                {
                    ProcessHealthThresholdTriggers();
                }
            }
        }

        #endregion

        #region Trigger Processing

        private void ProcessUnitTypeTriggers(Unit unit)
        {
            if (unit == null) return;

            string unitTypeName = unit.GetType().Name;
            string unitName = unit.name;

            ProcessTriggersForCondition(trigger =>
                trigger.triggerType == MusicTrigger.TriggerType.UnitType &&
                (trigger.unitTypeName == unitTypeName || trigger.unitTypeName == unitName));
        }

        private void ProcessTurnNumberTriggers(int totalTurnNumber, int turnNumber)
        {
            ProcessTriggersForCondition(trigger =>
            {
                if (trigger.triggerType != MusicTrigger.TriggerType.TurnNumber) return false;

                if (trigger.turnCountMode == MusicTrigger.TurnCountMode.CyclicTurns)
                {
                    if (trigger.everyNthTurn)
                        return turnNumber > 0 && turnNumber % trigger.turnNumber == 0;
                    else
                        return turnNumber == trigger.turnNumber;
                }
                else
                {
                    return totalTurnNumber == trigger.turnNumber;
                }
            });
        }

        private void ProcessCombatStateTriggers(CombatState state, Unit activeUnit = null)
        {
            ProcessTriggersForCondition(trigger =>
            {
                if (trigger.triggerType != MusicTrigger.TriggerType.CombatState) return false;
                if (trigger.combatState != state) return false;

                // If no unit filter is specified, trigger for any unit
                if (string.IsNullOrEmpty(trigger.combatStateUnitFilter)) return true;

                // If we don't have an active unit, we can't filter
                if (activeUnit == null) return false;

                // Check if the unit matches the filter (by name or type)
                string unitTypeName = activeUnit.GetType().Name;
                string unitName = activeUnit.name;

                return trigger.combatStateUnitFilter == unitTypeName ||
                       trigger.combatStateUnitFilter == unitName;
            });
        }

        private void ProcessHealthThresholdTriggers()
        {
            var units = UnitManager.AllUnits;

            foreach (var unit in units)
            {
                if (unit?.characterData == null) continue;

                float healthPercent = (float)unit.currentHealth / unit.characterData.maxHealth;

                if (healthPercent <= 0.3f)
                {
                    ProcessTriggersForCondition(trigger =>
                        trigger.triggerType == MusicTrigger.TriggerType.HealthThreshold &&
                        healthPercent <= trigger.healthPercentage);
                    break; // Only process once per frame
                }
            }
        }

        private void ProcessCustomEventTriggers(string eventName)
        {
            ProcessTriggersForCondition(trigger =>
                trigger.triggerType == MusicTrigger.TriggerType.CustomEvent &&
                trigger.eventName == eventName);
        }

        private void ProcessTriggersForCondition(System.Func<MusicTrigger, bool> condition)
        {
            if (currentTrack == null || !trackIsPlaying) return;

            foreach (var layer in currentTrack.layers)
            {
                // Check enable triggers
                foreach (var trigger in layer.enableTriggers)
                {
                    // Skip if this trigger should only activate once and has already been used
                    if (trigger.activateOnce && trigger.hasBeenActivated)
                        continue;

                    if (condition(trigger) && !layer.isEnabled)
                    {
                        // Debug.Log($"Music trigger activated: Enabling layer '{layer.layerName}' due to {trigger.triggerType} trigger");
                        EnableLayer(layer.layerName);

                        // Mark as activated if it's a one-time trigger
                        if (trigger.activateOnce)
                            trigger.hasBeenActivated = true;

                        break;
                    }
                }

                // Check disable triggers  
                foreach (var trigger in layer.disableTriggers)
                {
                    // Skip if this trigger should only activate once and has already been used
                    if (trigger.activateOnce && trigger.hasBeenActivated)
                        continue;

                    if (condition(trigger) && layer.isEnabled)
                    {
                        // Debug.Log($"Music trigger activated: Disabling layer '{layer.layerName}' due to {trigger.triggerType} trigger");
                        DisableLayer(layer.layerName);

                        // Mark as activated if it's a one-time trigger
                        if (trigger.activateOnce)
                            trigger.hasBeenActivated = true;

                        break;
                    }
                }
            }
        }

        #endregion

        #region Debug Methods

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugToggleLayer(string layerName)
        {
            if (layerLookup.TryGetValue(layerName, out MusicLayer layer))
            {
                if (layer.isEnabled)
                    DisableLayer(layerName);
                else
                    EnableLayer(layerName);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugTriggerEvent(string eventName)
        {
            TriggerCustomEvent(eventName);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugLogLayerStates()
        {
            if (currentTrack == null) return;

            Debug.Log("=== Music Layer States ===");
            foreach (var layer in currentTrack.layers)
            {
                string state = layer.isEnabled ? "ENABLED" : "DISABLED";
                float volume = layer.audioSource?.volume ?? 0f;
                bool playing = layer.audioSource?.isPlaying ?? false;

                Debug.Log($"{layer.layerName}: {state} | Volume: {volume:F2} | Playing: {playing}");
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugLogTriggerStates()
        {
            if (currentTrack == null) return;

            Debug.Log("=== Music Trigger States ===");
            foreach (var layer in currentTrack.layers)
            {
                Debug.Log($"Layer: {layer.layerName}");

                Debug.Log("  Enable Triggers:");
                foreach (var trigger in layer.enableTriggers)
                {
                    string activationState = trigger.activateOnce ?
                        (trigger.hasBeenActivated ? "USED" : "READY") : "REPEATABLE";
                    Debug.Log($"    {trigger.triggerType}: {activationState}");
                }

                Debug.Log("  Disable Triggers:");
                foreach (var trigger in layer.disableTriggers)
                {
                    string activationState = trigger.activateOnce ?
                        (trigger.hasBeenActivated ? "USED" : "READY") : "REPEATABLE";
                    Debug.Log($"    {trigger.triggerType}: {activationState}");
                }
            }
        }

        #endregion
    }
}