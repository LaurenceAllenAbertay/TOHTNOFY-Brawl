using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class GridManager : MonoBehaviour
    {
        #region Enums and Properties

        public enum HighlightMode
        {
            None,
            Movement,
            AbilityPreview
        }

        public static GridManager Instance { get; private set; }
        public IReadOnlyList<Tile> AllTiles => allTiles;

        #endregion

        #region Fields

        [Header("Grid Settings")]
        [SerializeField] private Vector3 tileSpacing = new Vector3(1.5f, 1f, 1.5f);
        [SerializeField] public LayerMask tileLayer;
        [SerializeField] private MapConfiguration mapConfiguration;

        private readonly List<Tile> allTiles = new List<Tile>();
        private HighlightMode currentHighlightMode = HighlightMode.None;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Singleton pattern setup
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Load tile spacing from MapConfiguration if available
            LoadTileSpacingFromMapConfig();

            BuildTileList();
        }

        #endregion

        #region Grid Setup and Validation

        /// <summary>
        /// Rebuilds the tile list by finding all Tile components in the scene
        /// </summary>
        public void BuildTileList()
        {
            allTiles.Clear();
            allTiles.AddRange(FindObjectsOfType<Tile>());

            // Auto-detect spacing if not manually set (check if any component is 0 or negative)
            if (tileSpacing.x <= 0 || tileSpacing.y <= 0 || tileSpacing.z <= 0)
            {
                ValidateGridSpacing();
            }
        }

        public void ValidateGridSpacing()
        {
            if (allTiles.Count < 2) return;

            float minDistanceX = float.MaxValue;
            float minDistanceZ = float.MaxValue;
            float minDistanceY = float.MaxValue;

            // Find minimum non-zero distance between any two tiles for each axis
            for (int i = 0; i < allTiles.Count; i++)
            {
                for (int j = i + 1; j < allTiles.Count; j++)
                {
                    Vector3 delta = allTiles[j].transform.position - allTiles[i].transform.position;

                    if (Mathf.Abs(delta.x) > 0.1f && Mathf.Abs(delta.x) < minDistanceX)
                        minDistanceX = Mathf.Abs(delta.x);

                    if (Mathf.Abs(delta.z) > 0.1f && Mathf.Abs(delta.z) < minDistanceZ)
                        minDistanceZ = Mathf.Abs(delta.z);

                    if (Mathf.Abs(delta.y) > 0.1f && Mathf.Abs(delta.y) < minDistanceY)
                        minDistanceY = Mathf.Abs(delta.y);
                }
            }

            // Update spacing with detected values
            if (minDistanceX < float.MaxValue) tileSpacing.x = Mathf.Round(minDistanceX * 10f) / 10f;
            if (minDistanceZ < float.MaxValue) tileSpacing.z = Mathf.Round(minDistanceZ * 10f) / 10f;
            if (minDistanceY < float.MaxValue) tileSpacing.y = Mathf.Round(minDistanceY * 10f) / 10f;

            Debug.Log($"Grid spacing detected as: X={tileSpacing.x}, Y={tileSpacing.y}, Z={tileSpacing.z}");
        }

        public Vector3 GetTileSpacing()
        {
            return tileSpacing;
        }

        #endregion

        #region Core Tile Finding


        /// <summary>
        /// Finds the tile at a specific world position using raycast with fallback to closest tile
        /// </summary>
        public Tile GetTileAtPosition(Vector3 worldPos)
        {
            // Primary method: raycast from above for precise detection
            RaycastHit hit;
            if (Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out hit, 20f, tileLayer))
            {
                return hit.collider.GetComponent<Tile>();
            }

            // Fallback: find closest tile that's snapped to the grid
            return GetClosestGridAlignedTile(worldPos);
        }

        /// <summary>
        /// Finds the closest tile that's properly aligned to the 3D grid spacing
        /// </summary>
        private Tile GetClosestGridAlignedTile(Vector3 worldPos)
        {
            // Snap the world position to the nearest grid point
            Vector3 snappedPos = new Vector3(
                Mathf.Round(worldPos.x / tileSpacing.x) * tileSpacing.x,
                Mathf.Round(worldPos.y / tileSpacing.y) * tileSpacing.y,
                Mathf.Round(worldPos.z / tileSpacing.z) * tileSpacing.z
            );

            // Find tile closest to this snapped position
            Tile bestTile = null;
            float bestDistance = float.MaxValue;

            foreach (var tile in allTiles)
            {
                float distance = Vector3.Distance(tile.transform.position, snappedPos);
                if (distance < bestDistance && distance < (tileSpacing.magnitude * 0.1f)) // Within 10% of spacing
                {
                    bestDistance = distance;
                    bestTile = tile;
                }
            }

            return bestTile;
        }

        /// <summary>
        /// Finds the closest tile to a world position within optional max distance
        /// </summary>
        public Tile GetClosestTile(Vector3 worldPos, Vector3 maxDistance)
        {
            Tile best = null;
            float bestDistance = float.PositiveInfinity;

            foreach (var tile in allTiles)
            {
                Vector3 diff = tile.transform.position - worldPos;

                // Check if within max distance bounds for each axis
                if (Mathf.Abs(diff.x) <= maxDistance.x &&
                    Mathf.Abs(diff.y) <= maxDistance.y &&
                    Mathf.Abs(diff.z) <= maxDistance.z)
                {
                    float distance = diff.magnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = tile;
                    }
                }
            }
            return best;
        }

        #endregion

        #region Directional Navigation
        public Tile GetTileInDirection(Tile fromTile, Vector2Int direction)
        {
            if (fromTile == null) return null;

            Vector3 targetPos = fromTile.transform.position;
            targetPos.x += direction.x * tileSpacing.x;
            targetPos.z += direction.y * tileSpacing.z;

            return GetTileAtPosition(targetPos);
        }

        #endregion

        #region Adjacency and Neighbors

        public List<Tile> GetAdjacentTiles(Tile centerTile, bool includeDiagonals = false)
        {
            if (centerTile == null) return new List<Tile>();

            var adjacent = new List<Tile>();
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            foreach (var dir in directions)
            {
                var tile = GetTileInDirection(centerTile, dir);
                // Only include tiles on the same Y level and that are passable
                if (tile != null && tile.passableTerrain && IsSameYLevel(centerTile, tile))
                    adjacent.Add(tile);
            }

            // Only include diagonals on the same Y level
            if (includeDiagonals)
            {
                Vector2Int[] diagonalDirections = {
            new Vector2Int(1, 1), new Vector2Int(-1, 1),
            new Vector2Int(1, -1), new Vector2Int(-1, -1)
        };

                foreach (var dir in diagonalDirections)
                {
                    var tile = GetTileInDirection(centerTile, dir);
                    if (tile != null && tile.passableTerrain && IsSameYLevel(centerTile, tile))
                        adjacent.Add(tile);
                }
            }

            return adjacent;
        }

        // Add to GridManager.cs
        public List<Tile> GetAllAdjacentTiles(Tile centerTile, bool includeDiagonals = false)
        {
            if (centerTile == null) return new List<Tile>();

            var adjacent = new List<Tile>();
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            foreach (var dir in directions)
            {
                var tile = GetTileInDirection(centerTile, dir);
                if (tile != null) // Remove the passableTerrain check
                    adjacent.Add(tile);
            }

            if (includeDiagonals)
            {
                Vector2Int[] diagonalDirections = {
            new Vector2Int(1, 1), new Vector2Int(-1, 1),
            new Vector2Int(1, -1), new Vector2Int(-1, -1)
        };

                foreach (var dir in diagonalDirections)
                {
                    var tile = GetTileInDirection(centerTile, dir);
                    if (tile != null)
                        adjacent.Add(tile);
                }
            }

            return adjacent;
        }

        /// <summary>
        /// Checks if two tiles are adjacent (within one grid spacing distance)
        /// </summary>
        public bool AreTilesAdjacent(Tile tile1, Tile tile2)
        {
            if (tile1 == null || tile2 == null) return false;

            Vector3 diff = tile2.transform.position - tile1.transform.position;

            // Check if tiles are adjacent in exactly one direction
            int nonZeroAxes = 0;
            if (Mathf.Abs(diff.x) > 0.1f) nonZeroAxes++;
            if (Mathf.Abs(diff.z) > 0.1f) nonZeroAxes++;
            if (Mathf.Abs(diff.y) > 0.1f) nonZeroAxes++;

            // Should be adjacent in exactly one axis and on same Y level for movement
            if (nonZeroAxes != 1) return false;

            // Check distances match spacing
            bool xMatch = Mathf.Abs(Mathf.Abs(diff.x) - tileSpacing.x) < 0.1f;
            bool zMatch = Mathf.Abs(Mathf.Abs(diff.z) - tileSpacing.z) < 0.1f;
            bool yMatch = Mathf.Abs(diff.y) < 0.1f; // Same Y level

            return (xMatch && yMatch) || (zMatch && yMatch);
        }

        #endregion

        #region Pathfinding

        /// <summary>
        /// Calculates all tiles reachable within movement range using breadth-first search
        /// Uses only orthogonal movement for pathfinding
        /// </summary>
        public List<Tile> GetReachableTiles(Tile startTile, int movementRange)
        {
            List<Tile> reachableTiles = new List<Tile>();
            Queue<(Tile tile, int distance)> queue = new Queue<(Tile, int)>();
            HashSet<Tile> visited = new HashSet<Tile>();

            queue.Enqueue((startTile, 0));
            visited.Add(startTile);

            while (queue.Count > 0)
            {
                var (current, distance) = queue.Dequeue();

                if (distance <= movementRange)
                {
                    reachableTiles.Add(current);

                    // Use orthogonal movement only for pathfinding
                    var adjacentTiles = GetAdjacentTiles(current, false);
                    foreach (Tile adjacent in adjacentTiles)
                    {
                        if (visited.Contains(adjacent)) continue;

                        // Tile must be moveable and either unoccupied or the start tile
                        bool passable = adjacent.moveable && (!adjacent.occupied || adjacent == startTile);
                        if (!passable) continue;

                        visited.Add(adjacent);
                        queue.Enqueue((adjacent, distance + 1));
                    }
                }
            }

            return reachableTiles;
        }

        /// <summary>
        /// Finds shortest path between two tiles using breadth-first search with max step limit
        /// Uses only orthogonal movement for pathfinding
        /// </summary>
        public List<Tile> FindPath(Tile start, Tile goal, int maxSteps = int.MaxValue)
        {
            var path = new List<Tile>();
            if (start == null || goal == null) return path;
            if (start == goal) return path;

            var cameFrom = new Dictionary<Tile, Tile>();
            var visited = new HashSet<Tile> { start };
            var queue = new Queue<Tile>();
            var depth = new Dictionary<Tile, int> { [start] = 0 };

            queue.Enqueue(start);
            bool found = false;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int steps = depth[current];

                if (steps >= maxSteps) continue;

                // Use orthogonal movement only for pathfinding
                var adjacentTiles = GetAdjacentTiles(current, false);
                foreach (var adjacent in adjacentTiles)
                {
                    if (adjacent == null || visited.Contains(adjacent)) continue;

                    // Goal tile can be occupied, others must be free
                    bool passable = adjacent.moveable && (adjacent == goal || !adjacent.occupied);
                    if (!passable) continue;

                    visited.Add(adjacent);
                    cameFrom[adjacent] = current;
                    depth[adjacent] = steps + 1;

                    if (adjacent == goal)
                    {
                        found = true;
                        queue.Clear();
                        break;
                    }

                    queue.Enqueue(adjacent);
                }
            }

            if (!found) return path;

            // Reconstruct path from goal back to start, then reverse
            var cur = goal;
            while (cur != start)
            {
                path.Add(cur);
                cur = cameFrom[cur];
            }
            path.Reverse();
            return path;
        }

        /// <summary>
        /// Finds shortest path between two tiles, optimized for visual movement
        /// Returns waypoints that allow for diagonal animation while respecting orthogonal pathfinding
        /// </summary>
        public List<Tile> FindPathOptimized(Tile start, Tile goal, int maxSteps = int.MaxValue)
        {
            if (start == null || goal == null) return new List<Tile>();
            if (start == goal) return new List<Tile>();

            // Find the standard orthogonal path first
            var fullPath = FindPath(start, goal, maxSteps);
            if (fullPath.Count == 0) return fullPath;

            // If the path is just one step, return it directly
            if (fullPath.Count == 1)
            {
                return new List<Tile> { fullPath[0] };
            }

            // Convert to waypoints with proper optimization
            return ConvertPathToWaypointsWithDiagonals(start, fullPath);
        }

        #endregion

        #region Path Optimization

        /// <summary>
        /// Converts an orthogonal path to waypoints with proper diagonal movement and straight line optimization
        /// </summary>
        private List<Tile> ConvertPathToWaypointsWithDiagonals(Tile start, List<Tile> fullPath)
        {
            if (fullPath.Count <= 1) return fullPath;

            List<Tile> waypoints = new List<Tile>();
            int currentIndex = 0;
            Tile currentTile = start;

            while (currentIndex < fullPath.Count)
            {
                // Try to find the farthest tile we can reach with a straight line or diagonal
                int farthestIndex = currentIndex;
                Tile farthestTile = currentIndex < fullPath.Count ? fullPath[currentIndex] : null;

                for (int lookahead = currentIndex + 1; lookahead < fullPath.Count; lookahead++)
                {
                    Tile candidateTile = fullPath[lookahead];
                    Vector2Int directionToCandidate = GetGridDirection(currentTile, candidateTile);

                    // Check if this is a valid straight move (same direction)
                    if (lookahead == currentIndex + 1)
                    {
                        // Always accept the immediate next tile
                        farthestIndex = lookahead;
                        farthestTile = candidateTile;
                        continue;
                    }

                    // Check if we can move directly to this tile via straight line
                    if (IsStraightLine(currentTile, fullPath, currentIndex, lookahead))
                    {
                        farthestIndex = lookahead;
                        farthestTile = candidateTile;
                    }
                    // Check if we can move directly via diagonal (exactly 1 tile in both directions)
                    else if (lookahead == currentIndex + 2 &&
                             IsValidDiagonalMove(currentTile, fullPath[currentIndex], candidateTile))
                    {
                        farthestIndex = lookahead;
                        farthestTile = candidateTile;
                        break; // Diagonal moves can only be 2 steps
                    }
                    else
                    {
                        // Can't reach further, break out
                        break;
                    }
                }

                // Add the farthest reachable tile as a waypoint
                if (farthestTile != null)
                {
                    waypoints.Add(farthestTile);
                    currentIndex = farthestIndex + 1; // Move past the tiles we just covered
                    currentTile = farthestTile;
                }
                else
                {
                    currentIndex++;
                }
            }

            return waypoints;
        }

        /// <summary>
        /// Checks if all tiles between start and end index form a straight line
        /// </summary>
        private bool IsStraightLine(Tile startTile, List<Tile> path, int startIndex, int endIndex)
        {
            if (endIndex <= startIndex + 1) return true;

            Vector2Int initialDirection = GetGridDirection(startTile, path[startIndex + 1]);

            for (int i = startIndex + 2; i <= endIndex; i++)
            {
                Vector2Int currentDirection = GetGridDirection(path[i - 1], path[i]);
                if (currentDirection != initialDirection)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if moving from start to end via intermediate tile forms a valid diagonal move
        /// </summary>
        private bool IsValidDiagonalMove(Tile start, Tile intermediate, Tile end)
        {
            // Check if this is a perfect L-shape: one horizontal + one vertical move
            Vector2Int dir1 = GetGridDirection(start, intermediate);
            Vector2Int dir2 = GetGridDirection(intermediate, end);

            bool isPerpendicular = IsPerpendicularMove(dir1, dir2);

            // Check if the diagonal tile exists and is reachable
            Vector2Int diagonalDir = dir1 + dir2;
            Tile diagonalTile = GetTileInDirection(start, diagonalDir);

            return isPerpendicular && diagonalTile != null && diagonalTile == end;
        }

        /// <summary>
        /// Calculates the grid direction between two tiles
        /// </summary>
        private Vector2Int GetGridDirection(Tile from, Tile to)
        {
            Vector3 delta = to.transform.position - from.transform.position;

            // Convert to grid coordinates using proper spacing
            int gridX = Mathf.RoundToInt(delta.x / tileSpacing.x);
            int gridZ = Mathf.RoundToInt(delta.z / tileSpacing.z);

            // Normalize to unit direction
            gridX = Mathf.Clamp(gridX, -1, 1);
            gridZ = Mathf.Clamp(gridZ, -1, 1);

            return new Vector2Int(gridX, gridZ);
        }

        /// <summary>
        /// Checks if two directions are perpendicular (one horizontal, one vertical)
        /// </summary>
        private bool IsPerpendicularMove(Vector2Int dir1, Vector2Int dir2)
        {
            // One direction is horizontal (x != 0, y == 0) and other is vertical (x == 0, y != 0)
            bool dir1Horizontal = (dir1.x != 0 && dir1.y == 0);
            bool dir1Vertical = (dir1.x == 0 && dir1.y != 0);
            bool dir2Horizontal = (dir2.x != 0 && dir2.y == 0);
            bool dir2Vertical = (dir2.x == 0 && dir2.y != 0);

            return (dir1Horizontal && dir2Vertical) || (dir1Vertical && dir2Horizontal);
        }

        #endregion

        #region Area Selection

        /// <summary>
        /// Gets all passable tiles within a rectangular area centered on a position
        /// </summary>
        public List<Tile> GetTilesInRect(Vector3 center, int width, int height)
        {
            var tiles = new List<Tile>();

            for (int x = -width / 2; x <= width / 2; x++)
            {
                for (int z = -height / 2; z <= height / 2; z++)
                {
                    Vector3 checkPos = center + new Vector3(x * tileSpacing.x, 0, z * tileSpacing.z);
                    var tile = GetTileAtPosition(checkPos);
                    if (tile != null && tile.passableTerrain)
                    {
                        tiles.Add(tile);
                    }
                }
            }

            return tiles;
        }

        /// <summary>
        /// Gets all passable tiles in a cross pattern extending in four cardinal directions
        /// </summary>
        public List<Tile> GetTilesInCross(Tile center, int range)
        {
            var tiles = new List<Tile>();
            if (center == null) return tiles;

            Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            foreach (var dir in directions)
            {
                Tile current = center;
                for (int i = 1; i <= range; i++)
                {
                    current = GetTileInDirection(current, dir);
                    if (current != null && current.passableTerrain)
                    {
                        tiles.Add(current);
                    }
                    else
                    {
                        break; // Stop extending in this direction if blocked
                    }
                }
            }

            return tiles;
        }

        /// <summary>
        /// Gets all tiles at exactly the specified distance from center (ring pattern)
        /// </summary>
        public List<Tile> GetTilesAtDistance(Tile center, int distance)
        {
            var tiles = new List<Tile>();
            if (center == null) return tiles;

            var queue = new Queue<(Tile tile, int dist)>();
            var visited = new HashSet<Tile>();

            queue.Enqueue((center, 0));
            visited.Add(center);

            while (queue.Count > 0)
            {
                var (current, dist) = queue.Dequeue();

                if (dist == distance)
                {
                    tiles.Add(current);
                }
                else if (dist < distance)
                {
                    var adjacent = GetAdjacentTiles(current);
                    foreach (var adj in adjacent)
                    {
                        if (!visited.Contains(adj))
                        {
                            visited.Add(adj);
                            queue.Enqueue((adj, dist + 1));
                        }
                    }
                }
            }

            return tiles;
        }

        #endregion

        #region Distance Calculation

        /// <summary>
        /// Calculates 3D Manhattan distance between two tiles including Y level differences
        /// Used for jump validation across multiple tile layers
        /// </summary>
        private int GetGridDistance3D(Tile tile1, Tile tile2)
        {
            if (tile1 == null || tile2 == null) return int.MaxValue;

            Vector3 pos1 = tile1.transform.position;
            Vector3 pos2 = tile2.transform.position;

            // Calculate distance in each axis using tile spacing
            int xDistance = Mathf.RoundToInt(Mathf.Abs(pos2.x - pos1.x) / tileSpacing.x);
            int zDistance = Mathf.RoundToInt(Mathf.Abs(pos2.z - pos1.z) / tileSpacing.z);
            int yDistance = Mathf.RoundToInt(Mathf.Abs(pos2.y - pos1.y) / tileSpacing.y);

            // Return Manhattan distance including Y component
            return xDistance + zDistance + yDistance;
        }

        public int GetGridDistance(Tile tile1, Tile tile2, bool includeYLevel = false)
        {
            if (includeYLevel)
            {
                return GetGridDistance3D(tile1, tile2);
            }

            if (tile1 == null || tile2 == null) return int.MaxValue;
            Vector3 pos1 = tile1.transform.position;
            Vector3 pos2 = tile2.transform.position;

            // Calculate distance in each axis using tile spacing
            int xDistance = Mathf.RoundToInt(Mathf.Abs(pos2.x - pos1.x) / tileSpacing.x);
            int zDistance = Mathf.RoundToInt(Mathf.Abs(pos2.z - pos1.z) / tileSpacing.z);

            // Default behavior: only X and Z distance for backward compatibility
            return xDistance + zDistance;
        }

        #endregion

        #region Highlighting System

        /// <summary>
        /// Sets the highlight mode and applies appropriate visual feedback
        /// </summary>
        public void SetHighlightMode(HighlightMode mode, Unit unit = null, Ability ability = null, Vector2Int aimDir = default)
        {
            ClearAllHighlights();
            currentHighlightMode = mode;

            switch (mode)
            {
                case HighlightMode.None:
                    ClearAllHighlights();
                    break;

                case HighlightMode.Movement:
                    if (unit != null)
                        HighlightMovement(unit);
                    break;

                case HighlightMode.AbilityPreview:
                    if (unit != null && ability != null)
                        HighlightAbilityPreview(unit, ability, aimDir);
                    break;
            }
        }

        /// <summary>
        /// Highlights all tiles within movement range of the unit
        /// </summary>
        private void HighlightMovement(Unit unit)
        {
            var reachableTiles = GetReachableTiles(unit.currentTile, unit.currentSpeed);
            foreach (var tile in reachableTiles)
            {
                tile.Highlight(TileHighlightType.Moveable);
            }
        }

        /// <summary>
        /// Highlights tiles affected by an ability, showing danger zones and valid targets
        /// </summary>
        private void HighlightAbilityPreview(Unit unit, Ability ability, Vector2Int aimDir)
        {
            if (ability.targeting is RandomAOETargeting)
                return;

            var ctx = new AbilityContext
            {
                caster = unit,
                ability = ability,
                aimDir = aimDir
            };

            var traverseTiles = ability.targeting.GetTraversal(ctx);
            int targetsHighlighted = 0;

            foreach (var tile in traverseTiles)
            {
                // Always highlight tiles in the path, even if they're gaps
                tile.Highlight(TileHighlightType.Danger);

                var u = tile.currentUnit;
                if (u != null)
                {
                    bool isAlly = u is EnemyUnit == unit is EnemyUnit;
                    bool canHit = (isAlly && ability.canHitAllies) || (!isAlly && ability.canHitEnemies);
                    if (canHit)
                    {
                        tile.Highlight(TileHighlightType.AttackRange);
                        targetsHighlighted++;

                        // Only stop if ability can't pass through units AND we're not going over gaps
                        if (!ability.passThroughUnits && tile.passableTerrain)
                            break;

                        // Stop if max targets reached
                        if (targetsHighlighted >= ability.maxTargets)
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Clears all tile highlights and resets visual state
        /// </summary>
        public void ClearAllHighlights()
        {
            foreach (Tile tile in allTiles)
            {
                tile.ResetHighlight();
            }
        }

        #endregion

        private void LoadTileSpacingFromMapConfig()
        {
            if (mapConfiguration != null)
            {
                tileSpacing = mapConfiguration.tileSpacing;
            }
            else
            {
                // Try to find MapConfiguration in the scene
                var mapManager = FindObjectOfType<MapManager>();
                if (mapManager != null && mapManager.CurrentConfiguration != null)
                {
                    mapConfiguration = mapManager.CurrentConfiguration;
                    tileSpacing = mapConfiguration.tileSpacing;
                }
            }
        }

        private bool IsSameYLevel(Tile tile1, Tile tile2)
        {
            if (tile1 == null || tile2 == null) return false;

            float yDiff = Mathf.Abs(tile1.transform.position.y - tile2.transform.position.y);
            return yDiff < (tileSpacing.y * 0.5f); // Within half a Y spacing unit
        }

        public int GetYLevel(Tile tile)
        {
            if (tile == null) return 0;
            return Mathf.RoundToInt(tile.transform.position.y / tileSpacing.y);
        }

        #region Editor Tools

#if UNITY_EDITOR
        [Header("Debug")]
        [SerializeField] private bool showGridGizmos = true;
        [SerializeField] private Color gridGizmoColor = new Color(0, 1, 0, 0.3f);

        /// <summary>
        /// Draws grid connections in Scene view for debugging
        /// </summary>
        void OnDrawGizmos()
        {
            if (!showGridGizmos || allTiles.Count == 0) return;

            Gizmos.color = gridGizmoColor;

            // Draw lines between adjacent tiles
            foreach (var tile in allTiles)
            {
                if (tile == null) continue;

                var adjacent = GetAdjacentTiles(tile);
                foreach (var adj in adjacent)
                {
                    if (adj != null)
                    {
                        Gizmos.DrawLine(tile.transform.position, adj.transform.position);
                    }
                }
            }
        }
     
#endif

        #endregion
    }
}