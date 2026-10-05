using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitMovementController : MonoBehaviour
    {
        public static UnitMovementController Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float movementSpeed = 4f;

        public float MovementSpeed => movementSpeed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        #region Public API
        
        public IEnumerator ExecuteAnimatedMovement(
            Unit movingUnit,
            Tile destination,
            List<Tile> waypoints = null,
            System.Action onMovementStarted = null,
            System.Action onMovementComplete = null)
        {
            if (movingUnit == null || destination == null) yield break;
            
            Tile originTile = movingUnit.currentTile;
            
            List<Tile> pathToUse = waypoints;
            if (pathToUse == null || pathToUse.Count == 0)
            {
                int moveRange = movingUnit.GetEffectiveMovementRange();
                pathToUse = GridManager.Instance.FindPathOptimized(movingUnit.currentTile, destination, moveRange);
                if (pathToUse.Count == 0) yield break;
            }

            onMovementStarted?.Invoke();

            var unitAnimator = movingUnit.GetComponent<UnitAnimator>();
            var spriteRenderers = movingUnit.GetComponentsInChildren<SpriteRenderer>(includeInactive: false);

            if (unitAnimator != null)
                unitAnimator.PlayMove();

            float totalDistance = CalculateTotalDistance(movingUnit.transform.position, pathToUse);
            float totalDuration = totalDistance / movementSpeed;

            yield return StartCoroutine(MoveAlongWaypoints(movingUnit, pathToUse, totalDuration, spriteRenderers));

            if (unitAnimator != null)
                unitAnimator.PlayIdle();

            movingUnit.SetCurrentTile(destination);
            
            if (pathToUse.Count >= 2)
            {
                Vector3 secondLast = pathToUse[pathToUse.Count - 2].transform.position;
                Vector3 last       = pathToUse[pathToUse.Count - 1].transform.position;
                float dx = last.x - secondLast.x;
                if (Mathf.Abs(dx) > 0.01f)
                    movingUnit.FaceDirection(dx > 0 ? Vector2Int.right : Vector2Int.left);
            }
            else if (pathToUse.Count == 1)
            {
                float dx = pathToUse[0].transform.position.x - originTile.transform.position.x;
                if (Mathf.Abs(dx) > 0.01f)
                    movingUnit.FaceDirection(dx > 0 ? Vector2Int.right : Vector2Int.left);
            }

            onMovementComplete?.Invoke();
        }

        #endregion

        #region Private Movement Coroutines

        public IEnumerator MoveAlongWaypoints(
            Unit movingUnit,
            List<Tile> waypoints,
            float totalDuration,
            SpriteRenderer[] spriteRenderers = null,
            bool playDustFX = true,
            bool claimTilesLogically = true)
        {
            if (movingUnit == null || waypoints == null || waypoints.Count == 0) yield break;

            AnimationCurve curve = movingUnit.GetMovementCurve();
            totalDuration = Mathf.Max(totalDuration, 0.001f);

            Vector3 startPos = movingUnit.transform.position;

            var points = new List<Vector3>(waypoints.Count + 1) { startPos };
            foreach (var wp in waypoints)
                points.Add(wp.transform.position);

            float totalDistance = 0f;
            for (int i = 0; i < points.Count - 1; i++)
                totalDistance += Vector3.Distance(points[i], points[i + 1]);
            totalDistance = Mathf.Max(totalDistance, 0.001f);

            var legEndIndices = new List<int>();
            for (int i = 0; i < waypoints.Count; i++)
            {
                if (i == waypoints.Count - 1)
                {
                    legEndIndices.Add(i);
                    continue;
                }

                Vector2Int dirIn = GridDirectionUtility.DirectionFromPositions(points[i], points[i + 1]);
                Vector2Int dirOut = GridDirectionUtility.DirectionFromPositions(points[i + 1], points[i + 2]);

                if (dirIn != dirOut)
                    legEndIndices.Add(i);
            }

            var dustFX = playDustFX ? movingUnit.GetComponent<UnitMoveDustFX>() : null;
            bool hasPlayedBurst = false;

            Vector3 legStartPos = startPos;
            int waypointCursor = 0;

            foreach (int legEndIndex in legEndIndices)
            {
                int legTileCount = legEndIndex - waypointCursor + 1;

                var legCumulative = new float[legTileCount];
                Vector3 prev = legStartPos;
                float legDistance = 0f;
                for (int k = 0; k < legTileCount; k++)
                {
                    Vector3 tilePos = waypoints[waypointCursor + k].transform.position;
                    legDistance += Vector3.Distance(prev, tilePos);
                    legCumulative[k] = legDistance;
                    prev = tilePos;
                }
                legDistance = Mathf.Max(legDistance, 0.001f);

                float legDuration = Mathf.Max((legDistance / totalDistance) * totalDuration, 0.001f);
                int nextTileToTrigger = 0;
                float elapsed = 0f;

                while (elapsed < legDuration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / legDuration);
                    float targetDistance = Mathf.Clamp01(curve.Evaluate(t)) * legDistance;

                    int segmentIndex = 0;
                    while (segmentIndex < legTileCount - 1 && targetDistance >= legCumulative[segmentIndex])
                        segmentIndex++;

                    float segStartDist = segmentIndex == 0 ? 0f : legCumulative[segmentIndex - 1];
                    Vector3 segStart = segmentIndex == 0 ? legStartPos : waypoints[waypointCursor + segmentIndex - 1].transform.position;
                    Vector3 segEnd = waypoints[waypointCursor + segmentIndex].transform.position;
                    float segLength = Mathf.Max(legCumulative[segmentIndex] - segStartDist, 0.001f);
                    float segT = Mathf.Clamp01((targetDistance - segStartDist) / segLength);

                    UpdateSpriteFacing(spriteRenderers, segStart, segEnd);
                    movingUnit.transform.position = Vector3.Lerp(segStart, segEnd, segT);

                    if (dustFX != null)
                    {
                        Vector2Int dustDirection = GridDirectionUtility.DirectionFromPositions(segStart, segEnd);
                        if (dustDirection != Vector2Int.zero)
                        {
                            if (!hasPlayedBurst)
                            {
                                dustFX.PlayBurst(dustDirection);
                                hasPlayedBurst = true;
                            }
                            dustFX.StartTrail(dustDirection);
                        }
                    }

                    while (nextTileToTrigger < legTileCount &&
                           targetDistance >= legCumulative[nextTileToTrigger] - 0.001f)
                    {
                        var tile = waypoints[waypointCursor + nextTileToTrigger];
                        if (claimTilesLogically)
                            movingUnit.SetCurrentTileLogical(tile);
                        nextTileToTrigger++;

                        if (tile.HasActiveEffects)
                            yield return StartCoroutine(tile.TriggerOnEnterEffects(movingUnit));

                        if (BigMomentSequencer.Instance != null)
                            yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());
                    }

                    yield return null;
                }

                while (nextTileToTrigger < legTileCount)
                {
                    var tile = waypoints[waypointCursor + nextTileToTrigger];
                    if (claimTilesLogically)
                        movingUnit.SetCurrentTileLogical(tile);
                    nextTileToTrigger++;

                    if (tile.HasActiveEffects)
                        yield return StartCoroutine(tile.TriggerOnEnterEffects(movingUnit));

                    if (BigMomentSequencer.Instance != null)
                        yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());
                }

                legStartPos = waypoints[legEndIndex].transform.position;
                movingUnit.transform.position = legStartPos;
                waypointCursor = legEndIndex + 1;
            }

            if (dustFX != null)
                dustFX.StopTrail();

            movingUnit.transform.position = waypoints[waypoints.Count - 1].transform.position;
        }

        #endregion

        #region Helpers

        private static float CalculateTotalDistance(Vector3 start, List<Tile> waypoints)
        {
            float total = 0f;
            Vector3 prev = start;
            foreach (var wp in waypoints)
            {
                total += Vector3.Distance(prev, wp.transform.position);
                prev = wp.transform.position;
            }
            return Mathf.Max(total, 0.001f);
        }

        private static void UpdateSpriteFacing(SpriteRenderer[] renderers, Vector3 from, Vector3 to)
        {
            if (renderers == null) return;
            Vector3 dir = (to - from).normalized;

            if (Mathf.Abs(dir.x) <= 0.1f) return;

            bool flipX = dir.x > 0;
            foreach (var sr in renderers)
            {
                if (sr == null) continue;
                sr.flipX = flipX;
            }
        }

        #endregion
    }
}