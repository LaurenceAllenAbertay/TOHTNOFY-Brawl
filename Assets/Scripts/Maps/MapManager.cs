using UnityEngine;
using static UnityEngine.Rendering.RayTracingAccelerationStructure;

namespace DDD.TNFY.BRAWL
{
    public class MapManager : MonoBehaviour
    {
        public static MapManager Instance { get; private set; }

        [Header("Current Map Settings")]
        [SerializeField] private MapConfiguration currentMapConfiguration;

        public MapConfiguration CurrentConfiguration => currentMapConfiguration;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            ApplyMapSettings();
        }

        private void ApplyMapSettings()
        {
            if (currentMapConfiguration == null)
            {
                Debug.LogWarning("No map settings assigned!");
                return;
            }
        }
    }
}