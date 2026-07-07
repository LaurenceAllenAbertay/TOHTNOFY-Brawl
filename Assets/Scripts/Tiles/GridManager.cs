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
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            
            LoadTileSpacingFromMapConfig();

            BuildTileList();
        }

        #endregion

        #region Grid Setup and Validation
        
        public void BuildTileList()
        {
            allTiles.Clear();
            allTiles.AddRange(FindObjectsByType<Tile>(FindObjectsSortMode.None));
            
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
            
            if (minDistanceX < float.MaxValue) tileSpacing.x = Mathf.Round(minDistanceX * 10f) / 10f;
            if (minDistanceZ < float.MaxValue) tileSpacing.z = Mathf.Round(minDistanceZ * 10f) / 10f;
            if (minDistanceY < float.MaxValue) tileSpacing.y = Mathf.Round(minDistanceY * 10f) / 10f;
            
        }

        public Vector3 GetTileSpacing()
        {
            return tileSpacing;
        }

        #endregion

        #region Core Tile Finding
        
        public Tile GetTileAtPosition(Vector3 worldPos)
        {
            RaycastHit hit;
            if (Physics.Raycast(worldPos + Vector3.up * 10f, Vector3.down, out hit, 20f, tileLayer))
            {
                return hit.collider.GetComponent<Tile>();
            }

            return GetClosestGridAlignedTile(worldPos);
        }
        
        public Tile GetTileAtScreenPosition(Camera camera, Vector2 screenPosition)
        {
            if (camera == null) return null;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, tileLayer))
            {
                return hit.collider.GetComponent<Tile>();
            }

            return null;
        }
        
        private Tile GetClosestGridAlignedTile(Vector3 worldPos)
        {
            Vector3 snappedPos = new Vector3(
                Mathf.Round(worldPos.x / tileSpacing.x) * tileSpacing.x,
                Mathf.Round(worldPos.y / tileSpacing.y) * tileSpacing.y,
                Mathf.Round(worldPos.z / tileSpacing.z) * tileSpacing.z
            );
            
            Tile bestTile = null;
            float bestDistance = float.MaxValue;

            foreach (var tile in allTiles)
            {
                float distance = Vector3.Distance(tile.transform.position, snappedPos);
                if (distance < bestDistance && distance < (tileSpacing.magnitude * 0.1f)) 
                {
                    bestDistance = distance;
                    bestTile = tile;
                }
            }

            return bestTile;
        }
        
        public Tile GetClosestTile(Vector3 worldPos, Vector3 maxDistance)
        {
            Tile best = null;
            float bestDistance = float.PositiveInfinity;

            foreach (var tile in allTiles)
            {
                Vector3 diff = tile.transform.position - worldPos;
                
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
 
                if (tile != null && tile.passableTerrain && IsSameYLevel(centerTile, tile))
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
                    if (tile != null && tile.passableTerrain && IsSameYLevel(centerTile, tile))
                        adjacent.Add(tile);
                }
            }

            return adjacent;
        }
        
        public List<Tile> GetAllAdjacentTiles(Tile centerTile, bool includeDiagonals = false)
        {
            if (centerTile == null) return new List<Tile>();

            var adjacent = new List<Tile>();
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

            foreach (var dir in directions)
            {
                var tile = GetTileInDirection(centerTile, dir);
                if (tile != null)
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
        
        public bool AreTilesAdjacent(Tile tile1, Tile tile2)
        {
            if (tile1 == null || tile2 == null) return false;

            Vector3 diff = tile2.transform.position - tile1.transform.position;
            
            int nonZeroAxes = 0;
            if (Mathf.Abs(diff.x) > 0.1f) nonZeroAxes++;
            if (Mathf.Abs(diff.z) > 0.1f) nonZeroAxes++;
            if (Mathf.Abs(diff.y) > 0.1f) nonZeroAxes++;

            if (nonZeroAxes != 1) return false;

            bool xMatch = Mathf.Abs(Mathf.Abs(diff.x) - tileSpacing.x) < 0.1f;
            bool zMatch = Mathf.Abs(Mathf.Abs(diff.z) - tileSpacing.z) < 0.1f;
            bool yMatch = Mathf.Abs(diff.y) < 0.1f; 

            return (xMatch && yMatch) || (zMatch && yMatch);
        }

        #endregion

        #region Pathfinding
        
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
                    
                    var adjacentTiles = GetAdjacentTiles(current, false);
                    foreach (Tile adjacent in adjacentTiles)
                    {
                        if (visited.Contains(adjacent)) continue;
                        
                        bool passable = adjacent.moveable && (!adjacent.occupied || adjacent == startTile);
                        if (!passable) continue;

                        visited.Add(adjacent);
                        queue.Enqueue((adjacent, distance + 1));
                    }
                }
            }

            return reachableTiles;
        }

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

                var adjacentTiles = GetAdjacentTiles(current, false);
                foreach (var adjacent in adjacentTiles)
                {
                    if (adjacent == null || visited.Contains(adjacent)) continue;
                    
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

            var cur = goal;
            while (cur != start)
            {
                path.Add(cur);
                cur = cameFrom[cur];
            }
            path.Reverse();
            return path;
        }

        public List<Tile> FindPathOptimized(Tile start, Tile goal, int maxSteps = int.MaxValue)
        {
            if (start == null || goal == null) return new List<Tile>();
            if (start == goal) return new List<Tile>();
            
            var fullPath = FindPath(start, goal, maxSteps);
            if (fullPath.Count == 0) return fullPath;
            
            if (fullPath.Count == 1)
            {
                return new List<Tile> { fullPath[0] };
            }
            
            return ConvertPathToWaypointsWithDiagonals(start, fullPath);
        }

        #endregion

        #region Path Optimization

        private List<Tile> ConvertPathToWaypointsWithDiagonals(Tile start, List<Tile> fullPath)
        {
            if (fullPath.Count <= 1) return fullPath;

            List<Tile> waypoints = new List<Tile>();
            int currentIndex = 0;
            Tile currentTile = start;

            while (currentIndex < fullPath.Count)
            {
                int farthestIndex = currentIndex;
                Tile farthestTile = currentIndex < fullPath.Count ? fullPath[currentIndex] : null;

                for (int lookahead = currentIndex + 1; lookahead < fullPath.Count; lookahead++)
                {
                    Tile candidateTile = fullPath[lookahead];
                    Vector2Int directionToCandidate = GetGridDirection(currentTile, candidateTile);
                    
                    if (lookahead == currentIndex + 1)
                    {
                        farthestIndex = lookahead;
                        farthestTile = candidateTile;
                        continue;
                    }
                    
                    if (IsStraightLine(currentTile, fullPath, currentIndex, lookahead))
                    {
                        farthestIndex = lookahead;
                        farthestTile = candidateTile;
                    }
                    else if (lookahead == currentIndex + 2 &&
                             IsValidDiagonalMove(currentTile, fullPath[currentIndex], candidateTile))
                    {
                        farthestIndex = lookahead;
                        farthestTile = candidateTile;
                        break; 
                    }
                    else
                    {
                        break;
                    }
                }
                
                if (farthestTile != null)
                {
                    waypoints.Add(farthestTile);
                    currentIndex = farthestIndex + 1; 
                    currentTile = farthestTile;
                }
                else
                {
                    currentIndex++;
                }
            }

            return waypoints;
        }
        
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
        
        private bool IsValidDiagonalMove(Tile start, Tile intermediate, Tile end)
        {
            Vector2Int dir1 = GetGridDirection(start, intermediate);
            Vector2Int dir2 = GetGridDirection(intermediate, end);

            bool isPerpendicular = IsPerpendicularMove(dir1, dir2);
            
            Vector2Int diagonalDir = dir1 + dir2;
            Tile diagonalTile = GetTileInDirection(start, diagonalDir);

            return isPerpendicular && diagonalTile != null && diagonalTile == end;
        }
        
        public Vector2Int GetGridDirection(Tile from, Tile to)
        {
            Vector3 delta = to.transform.position - from.transform.position;
            
            int gridX = Mathf.RoundToInt(delta.x / tileSpacing.x);
            int gridZ = Mathf.RoundToInt(delta.z / tileSpacing.z);
            
            gridX = Mathf.Clamp(gridX, -1, 1);
            gridZ = Mathf.Clamp(gridZ, -1, 1);

            return new Vector2Int(gridX, gridZ);
        }

        private bool IsPerpendicularMove(Vector2Int dir1, Vector2Int dir2)
        {
            bool dir1Horizontal = (dir1.x != 0 && dir1.y == 0);
            bool dir1Vertical = (dir1.x == 0 && dir1.y != 0);
            bool dir2Horizontal = (dir2.x != 0 && dir2.y == 0);
            bool dir2Vertical = (dir2.x == 0 && dir2.y != 0);

            return (dir1Horizontal && dir2Vertical) || (dir1Vertical && dir2Horizontal);
        }

        #endregion

        #region Area Selection
        
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
                        break;
                    }
                }
            }

            return tiles;
        }
        
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
        
        private int GetGridDistance3D(Tile tile1, Tile tile2)
        {
            if (tile1 == null || tile2 == null) return int.MaxValue;

            Vector3 pos1 = tile1.transform.position;
            Vector3 pos2 = tile2.transform.position;
            
            int xDistance = Mathf.RoundToInt(Mathf.Abs(pos2.x - pos1.x) / tileSpacing.x);
            int zDistance = Mathf.RoundToInt(Mathf.Abs(pos2.z - pos1.z) / tileSpacing.z);
            int yDistance = Mathf.RoundToInt(Mathf.Abs(pos2.y - pos1.y) / tileSpacing.y);

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
            
            int xDistance = Mathf.RoundToInt(Mathf.Abs(pos2.x - pos1.x) / tileSpacing.x);
            int zDistance = Mathf.RoundToInt(Mathf.Abs(pos2.z - pos1.z) / tileSpacing.z);
            
            return xDistance + zDistance;
        }

        #endregion

        #region Highlighting System
        
        public void SetHighlightMode(HighlightMode mode, Unit unit = null, Ability ability = null, Vector2Int aimDir = default, int movementRangeOverride = -1)
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
                        HighlightMovement(unit, movementRangeOverride);
                    break;

                case HighlightMode.AbilityPreview:
                    if (unit != null && ability != null)
                        HighlightAbilityPreview(unit, ability, aimDir);
                    break;
            }
        }
        
        private void HighlightMovement(Unit unit, int movementRangeOverride = -1)
        {
            int movementRange = movementRangeOverride >= 0 ? movementRangeOverride : unit.currentSpeed;
            var reachableTiles = GetReachableTiles(unit.currentTile, movementRange);
            foreach (var tile in reachableTiles)
            {
                tile.Highlight(TileHighlightType.Moveable);
            }
        }
        
        private void HighlightAbilityPreview(Unit unit, Ability ability, Vector2Int aimDir)
        {
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
                tile.Highlight(TileHighlightType.Danger);

                var u = tile.currentUnit;
                if (u != null)
                {
                    bool canHit;
                    if (u.IsNeutral)
                        canHit = ability.canTargetNeutral;
                    else
                    {
                        bool isAlly = u is EnemyUnit == unit is EnemyUnit;
                        canHit = (isAlly && ability.canHitAllies) || (!isAlly && ability.canHitEnemies);
                    }

                    if (canHit)
                    {
                        tile.Highlight(TileHighlightType.AttackRange);
                        targetsHighlighted++;
                        
                        if (!ability.passThroughUnits && tile.passableTerrain)
                            break;
                        
                        if (targetsHighlighted >= ability.maxTargets)
                            break;
                    }
                }
            }
        }
        
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
                var mapManager = FindAnyObjectByType<MapManager>();
                if (mapManager != null && mapManager.CurrentConfiguration != null)
                {
                    mapConfiguration = mapManager.CurrentConfiguration;
                    tileSpacing = mapConfiguration.tileSpacing;
                }
            }
        }

        public bool IsSameYLevel(Tile tile1, Tile tile2)
        {
            if (tile1 == null || tile2 == null) return false;

            float yDiff = Mathf.Abs(tile1.transform.position.y - tile2.transform.position.y);
            return yDiff < (tileSpacing.y * 0.5f);
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