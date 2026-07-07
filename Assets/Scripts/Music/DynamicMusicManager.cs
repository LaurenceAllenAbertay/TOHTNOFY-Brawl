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

        [Header("Sync Debugging")]
        [SerializeField] private bool logSyncDiagnostics = true;
        [SerializeField] private int driftThresholdSamples = 2048;
        
        private Queue<AudioSource> audioSourcePool = new Queue<AudioSource>();
        private List<AudioSource> activeAudioSources = new List<AudioSource>();
        
        private Dictionary<string, MusicLayer> layerLookup = new Dictionary<string, MusicLayer>();
        private bool trackIsPlaying = false;
        
        private double scheduledDspStartTime;
        private int lastKnownReferenceSamples; 
        private int lastKnownReferenceFrequency; 
        private readonly Dictionary<string, long> lastLayerOffsets = new Dictionary<string, long>();

        private int currentTurnNumber = 0;
        private int currentTotalTurnNumber = 0;
        private Unit lastActiveUnit;
        private CombatState lastCombatState = CombatState.WaitingForInput;

        public static event System.Action<string> OnCustomMusicEvent;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            InitializeObjectPool();

            if (enableDynamicMusic)
            {
                Initialize();
            }
        }

        private void InitializeObjectPool()
        {
            for (int i = 0; i < poolSize; i++)
            {
                GameObject poolObj = new GameObject($"AudioSource_Pool_{i}");
                poolObj.transform.SetParent(transform);

                AudioSource source = poolObj.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.volume = 0f;

                source.priority = 0;

                poolObj.SetActive(false); 
                audioSourcePool.Enqueue(source);
            }
        }

        private void Initialize()
        {
            SubscribeToEvents();
            
            if (currentTrack != null)
            {
                LoadTrack(currentTrack);
            }
        }

        private void Start()
        {
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
            
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();

            if (Instance == this)
                Instance = null;
        }

        private void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            LogSync($"AUDIO CONFIG CHANGED (deviceWasChanged={deviceWasChanged}) — audio engine restarted by Unity.");

            if (trackIsPlaying)
            {
                StartCoroutine(ResyncNextFrame());
            }
        }

        private IEnumerator ResyncNextFrame()
        {
            yield return null;
            ResyncAllLayers("audio configuration change");
        }
        
        private void OnApplicationFocus(bool hasFocus)
        {
            LogSync($"OnApplicationFocus({hasFocus})");

            if (hasFocus && trackIsPlaying)
                ResyncIfPlaybackInterrupted();
        }

        private void OnApplicationPause(bool isPaused)
        {
            LogSync($"OnApplicationPause({isPaused})");

            if (!isPaused && trackIsPlaying)
                ResyncIfPlaybackInterrupted();
        }

        private void ResyncIfPlaybackInterrupted()
        {
            if (currentTrack == null) return;

            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null && !layer.audioSource.isPlaying)
                {
                    ResyncAllLayers("playback interruption detected on focus/pause resume");
                    return;
                }
            }

            LogSync("Focus regained, all sources still playing — no resync needed.");
        }

        private void ResyncAllLayers(string reason)
        {
            if (currentTrack == null) return;

            int referenceSamples;
            int referenceFrequency;

            AudioSource reference = null;
            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null && layer.audioSource.isPlaying && layer.audioSource.clip != null)
                {
                    reference = layer.audioSource;
                    break;
                }
            }

            if (reference != null)
            {
                referenceSamples = reference.timeSamples;
                referenceFrequency = reference.clip.frequency;
            }
            else if (lastKnownReferenceFrequency > 0)
            {
                referenceSamples = lastKnownReferenceSamples;
                referenceFrequency = lastKnownReferenceFrequency;
                LogSync("No layer currently playing — resyncing from last known position.");
            }
            else
            {
                LogSync("Resync requested but no reference position available — restarting from 0.");
                referenceSamples = 0;
                referenceFrequency = 0;
            }

            foreach (var layer in currentTrack.layers)
                layer.audioSource?.Stop();

            double dspStartTime = AudioSettings.dspTime + 0.1;
            scheduledDspStartTime = dspStartTime;

            foreach (var layer in currentTrack.layers)
            {
                var source = layer.audioSource;
                if (source == null || source.clip == null) continue;
                
                if (referenceFrequency > 0)
                {
                    long target = (long)referenceSamples * source.clip.frequency / referenceFrequency;
                    source.timeSamples = (int)(target % source.clip.samples);
                }
                else
                {
                    source.timeSamples = 0;
                }

                source.PlayScheduled(dspStartTime);
            }

            lastLayerOffsets.Clear(); 
            LogSync($"Hard-resynced all layers ({reason}). Scheduled dspTime={dspStartTime:F4}");
        }

        private void UnsubscribeFromEvents()
        {
            TurnManager.OnTurnStarted -= OnTurnStarted;
            TurnManager.OnTurnEnded -= OnTurnEnded;
            TurnManager.OnTurnNumberChanged -= OnTurnNumberChanged;
            UnitManager.OnUnitDied -= OnUnitDied;

            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
        }

        #region Sync Diagnostics

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void LateUpdate()
        {
            if (!logSyncDiagnostics || !trackIsPlaying || currentTrack == null) return;
            
            AudioSource reference = null;
            foreach (var layer in currentTrack.layers)
            {
                var src = layer.audioSource;
                if (src != null && src.isPlaying && src.clip != null)
                {
                    reference = src;
                    break;
                }
            }
            if (reference == null) return;
            
            lastKnownReferenceSamples = reference.timeSamples;
            lastKnownReferenceFrequency = reference.clip.frequency;

            long referenceLen = reference.clip.samples;
            bool anyProblem = false;

            foreach (var layer in currentTrack.layers)
            {
                var src = layer.audioSource;
                if (src == null || src.clip == null || src == reference) continue;

                if (!src.isPlaying)
                {
                    if (!lastLayerOffsets.ContainsKey(layer.layerName) || lastLayerOffsets[layer.layerName] != long.MinValue)
                    {
                        LogSync($"LAYER STOPPED: '{layer.layerName}' is no longer playing!");
                        DumpLayerSnapshot("layer stopped");
                        lastLayerOffsets[layer.layerName] = long.MinValue;
                    }
                    continue;
                }
                
                long refInLayerDomain = (long)reference.timeSamples * src.clip.frequency / reference.clip.frequency;
                long diff = src.timeSamples - refInLayerDomain;
                long len = src.clip.samples;
                diff = ((diff % len) + len) % len;
                if (diff > len / 2) diff -= len;

                bool hadPrevious = lastLayerOffsets.TryGetValue(layer.layerName, out long previousDiff)
                                   && previousDiff != long.MinValue;
                
                if (hadPrevious && System.Math.Abs(diff - previousDiff) > driftThresholdSamples)
                {
                    anyProblem = true;
                    LogSync($"SUDDEN JUMP: '{layer.layerName}' offset changed {previousDiff} -> {diff} samples " +
                            $"({(diff - previousDiff) / (float)src.clip.frequency * 1000f:F1}ms shift) vs '{reference.clip.name}'");
                }

                else if (!hadPrevious && System.Math.Abs(diff) > driftThresholdSamples)
                {
                    anyProblem = true;
                    LogSync($"OUT OF SYNC: '{layer.layerName}' is {diff} samples " +
                            $"({diff / (float)src.clip.frequency * 1000f:F1}ms) off reference '{reference.clip.name}'");
                }

                lastLayerOffsets[layer.layerName] = diff;
            }

            if (anyProblem)
                DumpLayerSnapshot("desync detected");
        }

        private void DumpLayerSnapshot(string context)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"[MusicDebug] === Snapshot ({context}) ===");
            sb.AppendLine($"frame={Time.frameCount} realtime={Time.realtimeSinceStartup:F3} dspTime={AudioSettings.dspTime:F4} scheduledStart={scheduledDspStartTime:F4}");
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            sb.AppendLine($"dspBuffer={bufferLength}x{numBuffers} outputSampleRate={AudioSettings.outputSampleRate}");

            foreach (var layer in currentTrack.layers)
            {
                var src = layer.audioSource;
                if (src == null)
                {
                    sb.AppendLine($"  {layer.layerName}: <no source>");
                    continue;
                }
                sb.AppendLine($"  {layer.layerName}: playing={src.isPlaying} timeSamples={src.timeSamples}" +
                              $" clipSamples={(src.clip != null ? src.clip.samples : 0)}" +
                              $" freq={(src.clip != null ? src.clip.frequency : 0)}" +
                              $" vol={src.volume:F2} enabled={layer.isEnabled} priority={src.priority}");
            }

            Debug.LogWarning(sb.ToString());
        }
#endif

        private void LogSync(string message)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (logSyncDiagnostics)
                Debug.Log($"[MusicDebug] f{Time.frameCount} t{Time.realtimeSinceStartup:F3} dsp{AudioSettings.dspTime:F4} | {message}");
#endif
        }

        private void ValidateLayerSync()
        {
            if (currentTrack == null || currentTrack.layers.Count == 0) return;

            AudioClip first = null;
            string firstName = null;

            foreach (var layer in currentTrack.layers)
            {
                var clip = layer.audioClip;
                if (clip == null)
                {
                    Debug.LogWarning($"[MusicDebug] Layer '{layer.layerName}' has no AudioClip assigned.");
                    continue;
                }

                if (first == null)
                {
                    first = clip;
                    firstName = layer.layerName;
                }
                else if (clip.samples != first.samples || clip.frequency != first.frequency)
                {
                    Debug.LogError(
                        $"[MusicDebug] LOOP LENGTH MISMATCH: '{layer.layerName}' is {clip.samples} samples @ {clip.frequency}Hz " +
                        $"but '{firstName}' is {first.samples} @ {first.frequency}Hz. " +
                        "Unequal loop lengths WILL desync one loop boundary at a time. " +
                        "Fix the source audio or import settings (avoid MP3 — encoder padding changes length).");
                }

                if (!layer.looping)
                    Debug.LogWarning($"[MusicDebug] Layer '{layer.layerName}' has looping disabled in a synced track — it will fall silent and lose phase.");

                if (clip.loadState != AudioDataLoadState.Loaded)
                    Debug.LogWarning($"[MusicDebug] Clip '{clip.name}' not preloaded (loadState={clip.loadState}) — PlayScheduled may start late. " +
                        "Enable 'Preload Audio Data' in the clip's import settings, and avoid Load Type: Streaming for synced layers.");
            }
        }

        #endregion

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
            
            var turnManager = FindAnyObjectByType<TurnManager>();
            if (turnManager != null)
            {
                int totalTurns = turnManager.TotalTurnCount;
                int currentTurn = turnManager.GetTurnInCurrentRound();
                
                ProcessTurnNumberTriggers(totalTurns, currentTurn);
                
                currentTurnNumber = currentTurn;
                currentTotalTurnNumber = totalTurns;
            }
            
            ProcessCombatStateTriggers(CombatState.WaitingForInput, unit);
        }

        private void OnTurnEnded(Unit unit)
        {
            if (!enableDynamicMusic) return;

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
            
            StopCurrentTrack();

            currentTrack = track;
            layerLookup.Clear();
            lastLayerOffsets.Clear();
            lastKnownReferenceFrequency = 0;
            
            foreach (var layer in track.layers)
            {
                CreateLayerAudioSource(layer);
                layerLookup[layer.layerName] = layer;
            }

            ValidateLayerSync();
            
            StartAllLayersSynchronized();
        }

        public void StopCurrentTrack()
        {
            trackIsPlaying = false;

            for (int i = activeAudioSources.Count - 1; i >= 0; i--)
            {
                ReturnToPool(activeAudioSources[i]);
            }

            layerLookup.Clear();
            lastLayerOffsets.Clear();
        }

        public void EnableLayer(string layerName, float fadeTime = -1f)
        {
            if (!enableDynamicMusic || !layerLookup.TryGetValue(layerName, out MusicLayer layer))
                return;

            if (layer.isEnabled) return;

            float actualFadeTime = fadeTime >= 0 ? fadeTime : layer.fadeInDuration;

            LogSync($"EnableLayer('{layerName}') fade={actualFadeTime:F2}s");

            layer.isEnabled = true;
            FadeLayerVolume(layer, 0f, GetTargetVolume(layer), actualFadeTime);
        }

        public void DisableLayer(string layerName, float fadeTime = -1f)
        {
            if (!enableDynamicMusic || !layerLookup.TryGetValue(layerName, out MusicLayer layer))
                return;

            if (!layer.isEnabled) return;

            float actualFadeTime = fadeTime >= 0 ? fadeTime : layer.fadeOutDuration;

            LogSync($"DisableLayer('{layerName}') fade={actualFadeTime:F2}s");

            layer.isEnabled = false;
            FadeLayerVolume(layer, layer.audioSource.volume, 0f, actualFadeTime);
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

        private bool isMuted = false;
        private float volumeBeforeMute = -1f;

        public bool IsMuted => isMuted;
        
        public void SetMuted(bool muted)
        {
            if (muted == isMuted) return;

            isMuted = muted;

            if (muted)
            {
                volumeBeforeMute = globalMusicVolume;
                globalMusicVolume = 0f;
            }
            else
            {
                globalMusicVolume = volumeBeforeMute >= 0f ? volumeBeforeMute : 0.8f;
                volumeBeforeMute = -1f;
            }

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

            source.clip = layer.audioClip;
            source.loop = layer.looping;
            source.volume = 0f; 
            source.playOnAwake = false;

            layer.audioSource = source;
            layer.isEnabled = false; 
        }

        private void StartAllLayersSynchronized()
        {
            if (currentTrack == null) return;
            
            double dspStartTime = AudioSettings.dspTime + 0.2;
            scheduledDspStartTime = dspStartTime;

            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null)
                {
                    layer.audioSource.PlayScheduled(dspStartTime);
                }
            }

            trackIsPlaying = true;
            
            foreach (var layer in currentTrack.layers)
            {
                if (layer.audioSource != null)
                {
                    layer.isEnabled = layer.startEnabled;

                    if (layer.startEnabled)
                    {
                        FadeLayerVolume(layer, 0f, GetTargetVolume(layer), layer.fadeInDuration);
                    }
                    else
                    {
                        layer.audioSource.volume = 0f;
                    }
                }
            }

            LogSync($"Started track '{currentTrack.trackName}' with {currentTrack.layers.Count} layers, scheduled dspTime={dspStartTime:F4}");
        }

        private void FadeLayerVolume(MusicLayer layer, float fromVolume, float toVolume, float duration)
        {
            if (layer.audioSource == null) return;
            
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
                yield return new WaitForSeconds(0.5f);
                
                var combatManager = FindAnyObjectByType<CombatManager>();
                if (combatManager != null && combatManager.currentState != lastCombatState)
                {
                    ProcessCombatStateTriggers(combatManager.currentState, combatManager.CurrentActiveUnit);
                    lastCombatState = combatManager.currentState;
                }
                
                if (Time.frameCount % 120 == 0)
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
                
                if (string.IsNullOrEmpty(trigger.combatStateUnitFilter)) return true;
                
                if (activeUnit == null) return false;

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
                    break;
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
                foreach (var trigger in layer.enableTriggers)
                {
                    if (trigger.activateOnce && trigger.hasBeenActivated)
                        continue;

                    if (condition(trigger) && !layer.isEnabled)
                    {
                        EnableLayer(layer.layerName);

                        if (trigger.activateOnce)
                            trigger.hasBeenActivated = true;

                        break;
                    }
                }
                
                foreach (var trigger in layer.disableTriggers)
                {
                    if (trigger.activateOnce && trigger.hasBeenActivated)
                        continue;

                    if (condition(trigger) && layer.isEnabled)
                    {
                        DisableLayer(layer.layerName);
                        
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