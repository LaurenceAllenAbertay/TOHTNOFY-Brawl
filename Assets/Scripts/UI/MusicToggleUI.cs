using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Drives a UI Toggle that mutes / unmutes the DynamicMusicManager.
    ///
    /// Setup:
    ///   1. Add this component to the Toggle GameObject (or any GameObject in the scene).
    ///   2. Assign the Toggle reference in the Inspector.
    ///   3. The Toggle's On/Off visual state will match the muted state on Start.
    ///
    /// Toggle ON  = music is ON  (not muted).
    /// Toggle OFF = music is OFF (muted).
    /// </summary>
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

            // Sync the visual state to whatever the manager's current mute state is.
            SyncToggleToManager();

            // Subscribe — note we remove first to avoid double-subscription if
            // this component is ever disabled and re-enabled.
            musicToggle.onValueChanged.RemoveListener(OnToggleChanged);
            musicToggle.onValueChanged.AddListener(OnToggleChanged);
        }

        private void OnDestroy()
        {
            if (musicToggle != null)
                musicToggle.onValueChanged.RemoveListener(OnToggleChanged);
        }

        /// <summary>
        /// Called by the Toggle's onValueChanged event.
        /// isOn == true  → music should play (not muted).
        /// isOn == false → music should mute.
        /// </summary>
        private void OnToggleChanged(bool isOn)
        {
            if (DynamicMusicManager.Instance == null) return;

            DynamicMusicManager.Instance.SetMuted(!isOn);
        }

        /// <summary>
        /// Keeps the toggle visual in sync with the manager's actual mute state.
        /// Safe to call any time after Start.
        /// </summary>
        private void SyncToggleToManager()
        {
            if (DynamicMusicManager.Instance == null || musicToggle == null) return;

            // SetIsOnWithoutNotify prevents the listener from firing and creating
            // a feedback loop when we're only updating the visual state.
            musicToggle.SetIsOnWithoutNotify(!DynamicMusicManager.Instance.IsMuted);
        }
    }
}