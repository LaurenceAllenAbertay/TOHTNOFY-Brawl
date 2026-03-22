using DDD.TNFY.BRAWL;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "TNFY Brawl/Music Track")]
public class MusicTrack : ScriptableObject
{
    [Header("Track Info")]
    public string trackName;
    [TextArea(3, 5)] public string description;

    [Header("Timing")]
    public float bpm = 120f;
    public int beatsPerMeasure = 4;

    [Header("Music Layers")]
    public List<MusicLayer> layers = new List<MusicLayer>();

    [Header("Global Settings")]
    [Range(0f, 1f)] public float masterVolume = 0.7f;
    public bool syncAllLayers = true; // All layers start at the same time

    // Calculate beat duration for synchronization
    public float BeatDuration => 60f / bpm;
    public float MeasureDuration => BeatDuration * beatsPerMeasure;
}