using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum TriggerTiming
    {
        OnRoundEnd,
        OnEnter
    }

    public abstract class TileEffectData : ScriptableObject
    {
        [Header("Basic Info")]
        public string effectName = "Tile Effect";
        
        [TextArea(2, 4)]
        public string description = "";
        
        public Sprite icon;
        
        public Color effectColor = Color.white;

        [Header("Trigger")]
        public TriggerTiming triggerTiming = TriggerTiming.OnRoundEnd;

        [Header("Visual / Audio")]
        public GameObject applicationVFX;

        public GameObject persistentVFX;

        public GameObject removalVFX;

        public abstract IEnumerator Apply(TileEffectContext ctx);
        
        public abstract float GetAIDangerValue();
        
        public void SpawnApplicationVFX(Vector3 position)
        {
            if (applicationVFX != null)
                Object.Instantiate(applicationVFX, position, Quaternion.identity);
        }

        public GameObject SpawnPersistentVFX(Vector3 position)
        {
            if (persistentVFX == null) return null;
            return Object.Instantiate(persistentVFX, position, Quaternion.identity);
        }

        public void SpawnRemovalVFX(Vector3 position)
        {
            if (removalVFX != null)
                Object.Instantiate(removalVFX, position, Quaternion.identity);
        }
    }
}