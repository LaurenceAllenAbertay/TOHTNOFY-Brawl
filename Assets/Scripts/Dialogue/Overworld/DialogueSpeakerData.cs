using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class DialogueSpeakerData : ScriptableObject
    {
        public abstract string DisplayName { get; }

        public abstract Sprite SpeakerPortrait { get; }

        public abstract Color SpeakerColour { get; }
    }
}