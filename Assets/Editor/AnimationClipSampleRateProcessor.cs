using UnityEditor;
using UnityEngine;

namespace DDD.TNFY.BRAWL.EditorTools
{
    public class AnimationClipSampleRateProcessor : AssetModificationProcessor
    {
        private const float TargetSampleRate = 30f;

        static void OnWillCreateAsset(string path)
        {
            if (!path.EndsWith(".anim")) return;

            EditorApplication.delayCall += () => ApplySampleRate(path);
        }

        private static void ApplySampleRate(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) return;

            SetSampleRate(clip, TargetSampleRate);
        }

        private static void SetSampleRate(AnimationClip clip, float sampleRate)
        {
            var serializedClip = new SerializedObject(clip);
            var sampleRateProperty = serializedClip.FindProperty("m_SampleRate");

            if (sampleRateProperty == null)
            {
                Debug.LogWarning($"[AnimationClipSampleRateProcessor] Could not find m_SampleRate on {clip.name} — Unity may have changed the internal field name.");
                return;
            }

            sampleRateProperty.floatValue = sampleRate;
            serializedClip.ApplyModifiedProperties();
        }
    }
}