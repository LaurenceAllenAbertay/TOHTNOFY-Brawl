using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Pure static utility for grid-based direction math.
    ///
    /// All directional logic in ability effects (knockback, movement, charge) should
    /// route through here rather than re-implementing direction helpers locally.
    ///
    /// Conventions used throughout:
    ///   Vector2Int.up    = (0,  1) = North  (+Z in world space)
    ///   Vector2Int.right = (1,  0) = East   (+X in world space)
    ///   Vector2Int.down  = (0, -1) = South  (-Z in world space)
    ///   Vector2Int.left  = (-1, 0) = West   (-X in world space)
    ///   Clockwise when viewed from above: N → E → S → W → N
    /// </summary>
    public static class GridDirectionUtility
    {
        // ── Basic direction operations ─────────────────────────────────────────

        /// <summary>Returns the direction directly opposite to <paramref name="dir"/>.</summary>
        public static Vector2Int Opposite(Vector2Int dir)
            => new Vector2Int(-dir.x, -dir.y);

        /// <summary>
        /// Rotates <paramref name="dir"/> 90° clockwise when viewed from above.
        /// N→E, E→S, S→W, W→N
        /// </summary>
        public static Vector2Int RotateClockwise(Vector2Int dir)
            => new Vector2Int(dir.y, -dir.x);

        /// <summary>
        /// Rotates <paramref name="dir"/> 90° counter-clockwise when viewed from above.
        /// N→W, W→S, S→E, E→N
        /// </summary>
        public static Vector2Int RotateCounterClockwise(Vector2Int dir)
            => new Vector2Int(-dir.y, dir.x);

        /// <summary>
        /// Returns the two cardinal directions perpendicular to <paramref name="dir"/>:
        /// [clockwise perpendicular, counter-clockwise perpendicular].
        /// For North: [East, West]. For East: [South, North].
        /// </summary>
        public static Vector2Int[] Perpendiculars(Vector2Int dir)
            => new[] { RotateClockwise(dir), RotateCounterClockwise(dir) };

        /// <summary>True when <paramref name="dir"/> has both X and Y non-zero (diagonal).</summary>
        public static bool IsDiagonal(Vector2Int dir)
            => dir.x != 0 && dir.y != 0;

        /// <summary>
        /// For a diagonal direction, returns its two cardinal axis components.
        /// e.g. NE(1,1) → [E(1,0), N(0,1)]
        /// Only meaningful when called on a diagonal direction.
        /// </summary>
        public static Vector2Int[] CardinalComponents(Vector2Int diagonalDir)
            => new[] { new Vector2Int(diagonalDir.x, 0), new Vector2Int(0, diagonalDir.y) };

        // ── Tile-to-tile direction derivation ─────────────────────────────────

        /// <summary>
        /// Derives the grid direction from <paramref name="from"/> to <paramref name="to"/>.
        /// Each component is clamped to [-1, 1]. May return a diagonal direction if the tiles
        /// are not aligned on a single axis. Delegates to <see cref="GridManager.GetGridDirection"/>.
        /// </summary>
        public static Vector2Int FromTiles(Tile from, Tile to)
        {
            if (from == null || to == null || GridManager.Instance == null)
                return Vector2Int.zero;

            return GridManager.Instance.GetGridDirection(from, to);
        }

        /// <summary>
        /// Returns the dominant-axis cardinal direction from <paramref name="from"/> to
        /// <paramref name="to"/>. Always returns a pure cardinal (one component is always zero).
        /// When displacement is equal on both axes the X axis wins.
        ///
        /// Use this for clean single-axis push directions such as radial knockback
        /// (caster → target) or stomp knockback (origin tile → landing tile).
        /// </summary>
        public static Vector2Int CardinalFromTiles(Tile from, Tile to)
        {
            if (from == null || to == null || GridManager.Instance == null)
                return Vector2Int.zero;

            Vector3 delta = to.transform.position - from.transform.position;
            float absX = Mathf.Abs(delta.x);
            float absZ = Mathf.Abs(delta.z);

            // Guard against same tile
            if (absX < 0.001f && absZ < 0.001f)
                return Vector2Int.zero;

            return absX >= absZ
                ? new Vector2Int(delta.x > 0f ? 1 : -1, 0)
                : new Vector2Int(0, delta.z > 0f ? 1 : -1);
        }

        // ── Debug ─────────────────────────────────────────────────────────────

        /// <summary>Human-readable compass name for a cardinal or diagonal direction.</summary>
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

        // ── Knockback destination resolution ──────────────────────────────────

        /// <summary>
        /// Resolves the first valid destination tile for a unit knocked back from
        /// <paramref name="fromTile"/> in <paramref name="knockbackDir"/>.
        ///
        /// Cardinal fallback chain (e.g. pushed North):
        ///   1. North — free → go there.
        ///   2. East / West (laterals) — random if both free, forced if only one is.
        ///   3. North-East / North-West (forward diagonals) — same logic as step 2.
        ///   4. All five blocked → returns null (play animation, unit does not move).
        ///
        /// Diagonal fallback chain (e.g. pushed North-East):
        ///   1. North-East — free → go there.
        ///   2. East / North (the two cardinal components) — random if both free, forced if one.
        ///   3. All three blocked → returns null.
        ///
        /// A null return means the caller should play the knockback animation in place
        /// with no tile movement.
        /// </summary>
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
            // Step 1 — primary direction
            Tile direct = GetFreeKnockbackTile(fromTile, knockbackDir);
            if (direct != null) return direct;

            // Step 2 — laterals (perpendicular to knockback, same distance)
            Tile lateral = PickFreeFromDirections(
                fromTile,
                RotateClockwise(knockbackDir),
                RotateCounterClockwise(knockbackDir));
            if (lateral != null) return lateral;

            // Step 3 — forward diagonals (e.g. NE and NW when pushed North)
            Tile diagonal = PickFreeFromDirections(
                fromTile,
                knockbackDir + RotateClockwise(knockbackDir),
                knockbackDir + RotateCounterClockwise(knockbackDir));
            return diagonal; // null if all five directions are blocked
        }

        private static Tile ResolveDiagonalKnockback(Tile fromTile, Vector2Int knockbackDir)
        {
            // Step 1 — diagonal tile in knockback direction
            Tile direct = GetFreeKnockbackTile(fromTile, knockbackDir);
            if (direct != null) return direct;

            // Step 2 — the two cardinal components of the diagonal direction
            Vector2Int[] components = CardinalComponents(knockbackDir);
            Tile cardinal = PickFreeFromDirections(fromTile, components[0], components[1]);
            return cardinal; // null if all three directions are blocked
        }

        /// <summary>
        /// Checks <paramref name="dirA"/> and <paramref name="dirB"/> from
        /// <paramref name="fromTile"/>. Returns a random pick if both tiles are free,
        /// the free tile if only one is, or null if neither is available.
        /// </summary>
        private static Tile PickFreeFromDirections(Tile fromTile, Vector2Int dirA, Vector2Int dirB)
        {
            Tile tileA = GetFreeKnockbackTile(fromTile, dirA);
            Tile tileB = GetFreeKnockbackTile(fromTile, dirB);

            if (tileA != null && tileB != null)
                return Random.value < 0.5f ? tileA : tileB;

            return tileA ?? tileB;
        }

        /// <summary>
        /// Returns the tile one step from <paramref name="fromTile"/> in
        /// <paramref name="direction"/> if it is a valid knockback destination:
        /// exists, passable, unoccupied, and not at a higher Y level than the source.
        /// Returns null otherwise.
        /// </summary>
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