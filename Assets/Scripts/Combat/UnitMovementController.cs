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

            yield return StartCoroutine(MoveAlongWaypoints(movingUnit, pathToUse, spriteRenderers));

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

        private IEnumerator MoveAlongWaypoints(Unit movingUnit, List<Tile> waypoints, SpriteRenderer[] spriteRenderers)
        {
            Vector3 currentPos = movingUnit.transform.position;

            float totalDistance = CalculateTotalDistance(currentPos, waypoints);
            float totalTime = totalDistance / movementSpeed;

            var dustFX = movingUnit.GetComponent<UnitMoveDustFX>();
            bool hasPlayedBurst = false;

            foreach (var waypoint in waypoints)
            {
                Vector3 segmentStart = currentPos;
                Vector3 segmentEnd = waypoint.transform.position;
                float segmentDistance = Vector3.Distance(segmentStart, segmentEnd);
                float segmentTime = (segmentDistance / totalDistance) * totalTime;

                UpdateSpriteFacing(spriteRenderers, segmentStart, segmentEnd);

                Vector2Int segmentDirection = GridDirectionUtility.DirectionFromPositions(segmentStart, segmentEnd);
                if (dustFX != null && segmentDirection != Vector2Int.zero)
                {
                    if (!hasPlayedBurst)
                    {
                        dustFX.PlayBurst(segmentDirection);
                        hasPlayedBurst = true;
                    }
                    dustFX.StartTrail(segmentDirection);
                }

                float segmentElapsed = 0f;
                while (segmentElapsed < segmentTime)
                {
                    segmentElapsed += Time.deltaTime;
                    float smoothT = Mathf.SmoothStep(0f, 1f, segmentElapsed / segmentTime);
                    movingUnit.transform.position = Vector3.Lerp(segmentStart, segmentEnd, smoothT);
                    yield return null;
                }

                currentPos = segmentEnd;

                if (waypoint.HasActiveEffects)
                {
                    yield return StartCoroutine(waypoint.TriggerOnEnterEffects(movingUnit));

                    if (BigMomentSequencer.Instance != null)
                        yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());
                }
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