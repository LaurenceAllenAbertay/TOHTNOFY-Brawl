using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public static class GridDirectionUtility
    {
        public static Vector2Int Opposite(Vector2Int dir)
            => new Vector2Int(-dir.x, -dir.y);
        
        public static Vector2Int RotateClockwise(Vector2Int dir)
            => new Vector2Int(dir.y, -dir.x);
        
        public static Vector2Int RotateCounterClockwise(Vector2Int dir)
            => new Vector2Int(-dir.y, dir.x);
        
        public static Vector2Int[] Perpendiculars(Vector2Int dir)
            => new[] { RotateClockwise(dir), RotateCounterClockwise(dir) };

        public static bool IsDiagonal(Vector2Int dir)
            => dir.x != 0 && dir.y != 0;

        public static Vector2Int[] CardinalComponents(Vector2Int diagonalDir)
            => new[] { new Vector2Int(diagonalDir.x, 0), new Vector2Int(0, diagonalDir.y) };
        
        public static Vector2Int FromTiles(Tile from, Tile to)
        {
            if (from == null || to == null || GridManager.Instance == null)
                return Vector2Int.zero;

            return GridManager.Instance.GetGridDirection(from, to);
        }
        
        public static Vector2Int CardinalFromTiles(Tile from, Tile to)
        {
            if (from == null || to == null || GridManager.Instance == null)
                return Vector2Int.zero;

            return CardinalFromPositions(from.transform.position, to.transform.position);
        }

        public static Vector2Int CardinalFromPositions(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float absX = Mathf.Abs(delta.x);
            float absZ = Mathf.Abs(delta.z);
            
            if (absX < 0.001f && absZ < 0.001f)
                return Vector2Int.zero;

            return absX >= absZ
                ? new Vector2Int(delta.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, delta.z > 0f ? 1 : -1);
        }

        public static Vector2Int DirectionFromPositions(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float absX = Mathf.Abs(delta.x);
            float absZ = Mathf.Abs(delta.z);

            if (absX < 0.001f && absZ < 0.001f)
                return Vector2Int.zero;

            int x = absX < 0.001f ? 0 : (delta.x > 0f ? 1 : -1);
            int z = absZ < 0.001f ? 0 : (delta.z > 0f ? 1 : -1);
            return new Vector2Int(x, z);
        }

        public static string ToName(Vector2Int dir)
        {
            if (dir == new Vector2Int( 0,  1)) return "North";
            if (dir == new Vector2Int( 1,  0)) return "East";
            if (dir == new Vector2Int( 0, -1)) return "South";
            if (dir == new Vector2Int(-1,  0)) return "West";
            if (dir == new Vector2Int( 1,  1)) return "North-East";
            if (dir == new Vector2Int(-1,  1)) return "North-West";
            if (dir == new Vector2Int( 1, -1)) return "South-East";
            if (dir == new Vector2Int(-1, -1)) return "South-West";
            if (dir == Vector2Int.zero)        return "None";
            return $"({dir.x},{dir.y})";
        }
        
        public static Tile ResolveKnockbackDestination(Tile fromTile, Vector2Int knockbackDir)
        {
            if (fromTile == null || GridManager.Instance == null)
                return null;

            return IsDiagonal(knockbackDir)
                ? ResolveDiagonalKnockback(fromTile, knockbackDir)
                : ResolveCardinalKnockback(fromTile, knockbackDir);
        }

        private static Tile ResolveCardinalKnockback(Tile fromTile, Vector2Int knockbackDir)
        {
            Tile direct = GetFreeKnockbackTile(fromTile, knockbackDir);
            if (direct != null) return direct;
            
            Tile lateral = PickFreeFromDirections(
                fromTile,
                RotateClockwise(knockbackDir),
                RotateCounterClockwise(knockbackDir));
            if (lateral != null) return lateral;
            
            Tile diagonal = PickFreeFromDirections(
                fromTile,
                knockbackDir + RotateClockwise(knockbackDir),
                knockbackDir + RotateCounterClockwise(knockbackDir));
            return diagonal; 
        }

        private static Tile ResolveDiagonalKnockback(Tile fromTile, Vector2Int knockbackDir)
        {
            Tile direct = GetFreeKnockbackTile(fromTile, knockbackDir);
            if (direct != null) return direct;
            
            Vector2Int[] components = CardinalComponents(knockbackDir);
            Tile cardinal = PickFreeFromDirections(fromTile, components[0], components[1]);
            return cardinal;
        }

        private static Tile PickFreeFromDirections(Tile fromTile, Vector2Int dirA, Vector2Int dirB)
        {
            Tile tileA = GetFreeKnockbackTile(fromTile, dirA);
            Tile tileB = GetFreeKnockbackTile(fromTile, dirB);

            if (tileA != null && tileB != null)
                return Random.value < 0.5f ? tileA : tileB;

            return tileA ?? tileB;
        }

        private static Tile GetFreeKnockbackTile(Tile fromTile, Vector2Int direction)
        {
            Tile tile = GridManager.Instance.GetTileInDirection(fromTile, direction);
            if (tile == null)          return null;
            if (!tile.passableTerrain) return null;
            if (tile.occupied)         return null;
            if (GridManager.Instance.GetYLevel(tile) > GridManager.Instance.GetYLevel(fromTile))
                return null;
            return tile;
        }
    }
}