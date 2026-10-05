using System;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum PathStepType
    {
        Walk,
        Jump
    }

    public struct PathStep
    {
        public Tile tile;
        public PathStepType stepType;

        public PathStep(Tile tile, PathStepType stepType)
        {
            this.tile     = tile;
            this.stepType = stepType;
        }
    }

    public static class JumpAwarePathfinder
    {
        public const int WalkStepCost = 1;
        public const int JumpStepCost = JumpSystem.MinJumpRange;

        private struct StateKey : IEquatable<StateKey>
        {
            public readonly Tile tile;
            public readonly int  cost;

            public StateKey(Tile tile, int cost)
            {
                this.tile = tile;
                this.cost = cost;
            }

            public bool Equals(StateKey other) => tile == other.tile && cost == other.cost;

            public override bool Equals(object obj) => obj is StateKey other && Equals(other);

            public override int GetHashCode()
            {
                int tileHash = tile != null ? tile.GetHashCode() : 0;
                return (tileHash * 397) ^ cost;
            }
        }

        private struct StateQuality
        {
            public int effectTiles;
            public int jumps;
        }

        private struct ParentLink
        {
            public StateKey     parent;
            public PathStepType stepType;
        }

        private struct Move
        {
            public Tile         tile;
            public int          cost;
            public PathStepType stepType;
        }

        public static bool TryFindPath(Tile start, Tile goal, int maxCost, out List<PathStep> path)
        {
            path = null;

            var grid = GridManager.Instance;
            if (grid == null || start == null || goal == null || start == goal || maxCost <= 0)
                return false;

            var quality = new Dictionary<StateKey, StateQuality>();
            var parents = new Dictionary<StateKey, ParentLink>();
            var open    = new List<StateKey>();

            var walkCache = new Dictionary<Tile, List<Move>>();
            var jumpCache = new Dictionary<Tile, List<Move>>();

            var startKey = new StateKey(start, 0);
            quality[startKey] = new StateQuality();
            open.Add(startKey);

            bool found = false;
            StateKey goalKey = default;

            while (open.Count > 0)
            {
                int bestIndex = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    if (ComparePopOrder(open[i], quality[open[i]], open[bestIndex], quality[open[bestIndex]]) < 0)
                        bestIndex = i;
                }

                StateKey currentKey     = open[bestIndex];
                StateQuality currentVal = quality[currentKey];
                open.RemoveAt(bestIndex);

                if (currentKey.tile == goal)
                {
                    goalKey = currentKey;
                    found   = true;
                    break;
                }

                foreach (var move in GetMoves(grid, currentKey.tile, start, goal, walkCache, jumpCache))
                {
                    int nextCost = currentKey.cost + move.cost;
                    if (nextCost > maxCost) continue;

                    var nextVal = new StateQuality
                    {
                        effectTiles = currentVal.effectTiles + (move.tile.HasActiveEffects ? 1 : 0),
                        jumps       = currentVal.jumps + (move.stepType == PathStepType.Jump ? 1 : 0)
                    };

                    var nextKey = new StateKey(move.tile, nextCost);

                    if (quality.TryGetValue(nextKey, out StateQuality existing) && CompareQuality(existing, nextVal) <= 0)
                        continue;

                    quality[nextKey] = nextVal;
                    parents[nextKey] = new ParentLink { parent = currentKey, stepType = move.stepType };

                    if (!open.Contains(nextKey))
                        open.Add(nextKey);
                }
            }

            if (!found) return false;

            var reversed = new List<PathStep>();
            StateKey cursor = goalKey;

            while (parents.TryGetValue(cursor, out ParentLink link))
            {
                reversed.Add(new PathStep(cursor.tile, link.stepType));
                cursor = link.parent;
            }

            reversed.Reverse();

            if (reversed.Count == 0) return false;

            path = reversed;
            return true;
        }

        private static int ComparePopOrder(StateKey aKey, StateQuality aVal, StateKey bKey, StateQuality bVal)
        {
            if (aVal.effectTiles != bVal.effectTiles) return aVal.effectTiles - bVal.effectTiles;
            if (aKey.cost != bKey.cost)               return aKey.cost - bKey.cost;
            return aVal.jumps - bVal.jumps;
        }

        private static int CompareQuality(StateQuality a, StateQuality b)
        {
            if (a.effectTiles != b.effectTiles) return a.effectTiles - b.effectTiles;
            return a.jumps - b.jumps;
        }

        private static IEnumerable<Move> GetMoves(
            GridManager grid,
            Tile from,
            Tile start,
            Tile goal,
            Dictionary<Tile, List<Move>> walkCache,
            Dictionary<Tile, List<Move>> jumpCache)
        {
            foreach (var move in GetWalkMoves(grid, from, start, goal, walkCache))
                yield return move;

            foreach (var move in GetJumpMoves(grid, from, start, goal, jumpCache))
                yield return move;
        }

        private static List<Move> GetWalkMoves(
            GridManager grid, Tile from, Tile start, Tile goal, Dictionary<Tile, List<Move>> cache)
        {
            if (cache.TryGetValue(from, out List<Move> cached)) return cached;

            var moves = new List<Move>();

            foreach (var neighbour in grid.GetAdjacentTiles(from))
            {
                if (!IsUsableTile(neighbour, start, goal)) continue;

                moves.Add(new Move
                {
                    tile     = neighbour,
                    cost     = WalkStepCost,
                    stepType = PathStepType.Walk
                });
            }

            cache[from] = moves;
            return moves;
        }

        private static List<Move> GetJumpMoves(
            GridManager grid, Tile from, Tile start, Tile goal, Dictionary<Tile, List<Move>> cache)
        {
            if (cache.TryGetValue(from, out List<Move> cached)) return cached;

            var moves = new List<Move>();

            foreach (var candidate in grid.AllTiles)
            {
                if (candidate == from) continue;
                if (grid.GetGridDistance(from, candidate, includeYLevel: true) != JumpSystem.MinJumpRange) continue;
                if (!IsUsableTile(candidate, start, goal)) continue;
                if (JumpSystem.IsJumpBlockedByWalls(from, candidate)) continue;

                moves.Add(new Move
                {
                    tile     = candidate,
                    cost     = JumpStepCost,
                    stepType = PathStepType.Jump
                });
            }

            cache[from] = moves;
            return moves;
        }

        private static bool IsUsableTile(Tile tile, Tile start, Tile goal)
        {
            if (tile == null) return false;
            if (!tile.passableTerrain) return false;
            if (tile == start || tile == goal) return true;

            return !tile.occupied;
        }
    }
}