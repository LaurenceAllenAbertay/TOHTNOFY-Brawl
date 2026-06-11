using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Map Settings")]
    public class MapConfiguration : ScriptableObject
    {
        [Header("Map Info")]
        public string mapName = "Unnamed Map";
        [TextArea(3, 5)]
        public string mapDescription;

        [Header("Tile Settings")]
        public Vector3 tileSpacing = new Vector3(1.5f, 1.5f, 3f);

        [Header("Camera Settings")]
        public Vector3 defaultCameraPosition = new Vector3(0, 2, -4);
        public Vector3 defaultCameraRotation = new Vector3(20, 0, 0);
    }
}