using UnityEngine;
using UnityEditor;

public static class AnimationRootCurveRetargeter
{
    private const string TargetChildPath = "Sprite";

    [MenuItem("Tools/DDD/Retarget Root Curves To Child")]
    private static void RetargetSelectedClips()
    {
        var clips = Selection.GetFiltered<AnimationClip>(SelectionMode.DeepAssets | SelectionMode.Assets);

        if (clips.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Retarget Root Curves",
                "Select one or more AnimationClip assets (or a folder containing them) in the Project window first.",
                "OK");
            return;
        }

        bool proceed = EditorUtility.DisplayDialog(
            "Retarget Root Curves",
            $"This will move every Transform and SpriteRenderer curve currently bound to each clip's root object onto the child path '{TargetChildPath}', across {clips.Length} clip(s). Make sure your work is committed before continuing. Continue?",
            "Retarget",
            "Cancel");

        if (!proceed) return;

        int clipsChanged = 0;

        foreach (var clip in clips)
        {
            if (RetargetClip(clip))
                clipsChanged++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"Retargeted root curves to '{TargetChildPath}' on {clipsChanged} of {clips.Length} clip(s).");
    }

    private static bool RetargetClip(AnimationClip clip)
    {
        bool changed = false;

        Undo.RecordObject(clip, "Retarget root curves to child");

        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.path != string.Empty) continue;
            if (binding.type != typeof(Transform) && binding.type != typeof(SpriteRenderer)) continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);

            var newBinding = binding;
            newBinding.path = TargetChildPath;

            AnimationUtility.SetEditorCurve(clip, binding, null);
            AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            changed = true;
        }

        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (binding.path != string.Empty) continue;
            if (binding.type != typeof(SpriteRenderer)) continue;

            var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);

            var newBinding = binding;
            newBinding.path = TargetChildPath;

            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
            changed = true;
        }

        return changed;
    }
}