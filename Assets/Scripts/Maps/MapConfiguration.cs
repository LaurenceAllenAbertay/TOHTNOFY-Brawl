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

        [Header("Facing Direction")]
        [Tooltip("The natural facing direction for PlayerUnits. EnemyUnits will face the opposite.")]
        public FacingDirection playerNaturalFacing = FacingDirection.Right;

        [Header("Tile Settings")]
        public Vector3 tileSpacing = new Vector3(1.5f, 1.5f, 3f);

        [Header("Camera Settings")]
        public Vector3 defaultCameraPosition = new Vector3(0, 2, -4);
        public Vector3 defaultCameraRotation = new Vector3(20, 0, 0);

        public enum FacingDirection
        {
            Left,
            Right,
            Up,
            Down
        }

        // Helper method to get the opposite direction
        public FacingDirection GetEnemyNaturalFacing()
        {
            switch (playerNaturalFacing)
            {
                case FacingDirection.Left: return FacingDirection.Right;
                case FacingDirection.Right: return FacingDirection.Left;
                case FacingDirection.Up: return FacingDirection.Down;
                case FacingDirection.Down: return FacingDirection.Up;
                default: return FacingDirection.Left;
            }
        }

        // Convert to Vector2Int for ability aiming
        public Vector2Int GetFacingAsVector2Int(bool isPlayerUnit)
        {
            FacingDirection facing = isPlayerUnit ? playerNaturalFacing : GetEnemyNaturalFacing();

            switch (facing)
            {
                case FacingDirection.Left: return Vector2Int.left;
                case FacingDirection.Right: return Vector2Int.right;
                case FacingDirection.Up: return Vector2Int.up;
                case FacingDirection.Down: return Vector2Int.down;
                default: return Vector2Int.right;
            }
        }
    }
}