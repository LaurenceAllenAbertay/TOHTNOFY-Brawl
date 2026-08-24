using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(Animator))]
    public class EffectCueRelay : MonoBehaviour
    {
        public event System.Action<int> OnCue;

        public void AnimEvent_Cue0() => OnCue?.Invoke(0);
        public void AnimEvent_Cue1() => OnCue?.Invoke(1);
        public void AnimEvent_Cue2() => OnCue?.Invoke(2);
        public void AnimEvent_Cue3() => OnCue?.Invoke(3);
    }
}