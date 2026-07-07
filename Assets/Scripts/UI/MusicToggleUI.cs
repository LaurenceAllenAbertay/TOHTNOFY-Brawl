using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class MusicToggleUI : MonoBehaviour
    {
        [SerializeField] private Toggle musicToggle;

        private void Start()
        {
            if (musicToggle == null)
            {
                Debug.LogError("MusicToggleUI: musicToggle reference is not set!", this);
                return;
            }
            
            SyncToggleToManager();
            
            musicToggle.onValueChanged.RemoveListener(OnToggleChanged);
            musicToggle.onValueChanged.AddListener(OnToggleChanged);
        }

        private void OnDestroy()
        {
            if (musicToggle != null)
                musicToggle.onValueChanged.RemoveListener(OnToggleChanged);
        }

        private void OnToggleChanged(bool isOn)
        {
            if (DynamicMusicManager.Instance == null) return;

            DynamicMusicManager.Instance.SetMuted(!isOn);
        }

        private void SyncToggleToManager()
        {
            if (DynamicMusicManager.Instance == null || musicToggle == null) return;
            
            musicToggle.SetIsOnWithoutNotify(!DynamicMusicManager.Instance.IsMuted);
        }
    }
}