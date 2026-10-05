using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Dialogue/NPC Speaker")]
    public class NpcSpeakerData : DialogueSpeakerData
    {
        [Header("Identity")]
        public string speakerName;

        public Sprite portrait;

        public Color characterColour = Color.white;

        public override string DisplayName => speakerName;

        public override Sprite SpeakerPortrait => portrait;

        public override Color SpeakerColour => characterColour;
    }
}