using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Singleton that owns all unit movement animation.
    /// CombatManager calls ExecuteAnimatedMovement for player movement and provides
    /// onMovementStarted / onMovementComplete callbacks to manage its own state flags.
    /// UnitAI calls ExecuteAnimatedMovement directly (without callbacks) for AI movement.
    /// Attach to the same manager GameObject as CombatManager.
    /// </summary>
    public class UnitMovementController : MonoBehaviour
    {
        public static UnitMovementController Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private float movementSpeed = 4f;

        private CameraController cameraController;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            cameraController = FindAnyObjectByType<CameraController>();
        }

        #region Public API

        /// <summary>
        /// Animates movingUnit along the supplied waypoints to destination.
        /// </summary>
        /// <param name="movingUnit">The unit to move.</param>
        /// <param name="destination">Final logical tile.</param>
        /// <param name="waypoints">Pre-computed path (optimised). If null a path is calculated internally.</param>
        /// <param name="followCameraForAI">True for enemy units whose movement the player should watch.</param>
        /// <param name="onMovementStarted">Optional callback invoked just before animation begins.</param>
        /// <param name="onMovementComplete">Optional callback invoked after animation and tile update finish.</param>
        public IEnumerator ExecuteAnimatedMovement(
            Unit movingUnit,
            Tile destination,
            List<Tile> waypoints = null,
            bool followCameraForAI = false,
            System.Action onMovementStarted = null,
            System.Action onMovementComplete = null)
        {
            if (movingUnit == null || destination == null) yield break;

            // Capture the origin tile before movement begins — used for final facing on single-step paths.
            Tile originTile = movingUnit.currentTile;

            // Build path if not provided
            List<Tile> pathToUse = waypoints;
            if (pathToUse == null || pathToUse.Count == 0)
            {
                int moveRange = movingUnit.GetEffectiveMovementRange();
                pathToUse = GridManager.Instance.FindPathOptimized(movingUnit.currentTile, destination, moveRange);
                if (pathToUse.Count == 0) yield break;
            }

            onMovementStarted?.Invoke();

            var unitAnimator = movingUnit.GetComponent<UnitAnimator>();
            var spriteRenderer = movingUnit.GetComponentInChildren<SpriteRenderer>();

            if (unitAnimator != null)
                unitAnimator.PlayMove();

            if (followCameraForAI)
                yield return StartCoroutine(MoveAlongWaypointsWithCamera(movingUnit, pathToUse, spriteRenderer));
            else
                yield return StartCoroutine(MoveAlongWaypoints(movingUnit, pathToUse, spriteRenderer));

            if (unitAnimator != null)
                unitAnimator.PlayIdle();

            movingUnit.SetCurrentTile(destination);

            // Persist the final facing direction through FaceDirection so currentFacing
            // stays in sync with the flipX that UpdateSpriteFacing set during the walk.
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
                // Single-step path: use the captured origin tile so we aren't reading
                // a position that SetCurrentTile has already snapped to the destination.
                float dx = pathToUse[0].transform.position.x - originTile.transform.position.x;
                if (Mathf.Abs(dx) > 0.01f)
                    movingUnit.FaceDirection(dx > 0 ? Vector2Int.right : Vector2Int.left);
            }

            onMovementComplete?.Invoke();
        }

        #endregion

        #region Private Movement Coroutines

        private IEnumerator MoveAlongWaypoints(Unit movingUnit, List<Tile> waypoints, SpriteRenderer spriteRenderer)
        {
            Vector3 currentPos = movingUnit.transform.position;

            float totalDistance = CalculateTotalDistance(currentPos, waypoints);
            float totalTime = totalDistance / movementSpeed;

            foreach (var waypoint in waypoints)
            {
                Vector3 segmentStart = currentPos;
                Vector3 segmentEnd = waypoint.transform.position;
                float segmentDistance = Vector3.Distance(segmentStart, segmentEnd);
                float segmentTime = (segmentDistance / totalDistance) * totalTime;

                UpdateSpriteFacing(spriteRenderer, segmentStart, segmentEnd);

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
                    yield return StartCoroutine(waypoint.TriggerOnEnterEffects(movingUnit));
            }

            movingUnit.transform.position = waypoints[waypoints.Count - 1].transform.position;
        }

        private IEnumerator MoveAlongWaypointsWithCamera(Unit movingUnit, List<Tile> waypoints, SpriteRenderer spriteRenderer)
        {
            Vector3 currentPos = movingUnit.transform.position;

            float totalDistance = CalculateTotalDistance(currentPos, waypoints);
            float totalTime = totalDistance / movementSpeed;

            bool shouldMoveCamera = cameraController != null;
            Vector3 cameraStartPos = Vector3.zero;
            Vector3 cameraTargetPos = Vector3.zero;

            if (shouldMoveCamera)
            {
                cameraStartPos = cameraController.transform.position;
                Vector3 finalPos = waypoints[waypoints.Count - 1].transform.position;
                cameraTargetPos = cameraController.UnitFocusPosition(movingUnit);
                // Use the final waypoint position directly rather than the unit's current tile
                cameraTargetPos = cameraController.ClampToBounds(new Vector3(
                    finalPos.x,
                    finalPos.y + 2f,
                    finalPos.z - 3.5f));
            }

            float totalElapsed = 0f;

            foreach (var waypoint in waypoints)
            {
                Vector3 segmentStart = currentPos;
                Vector3 segmentEnd = waypoint.transform.position;
                float segmentDistance = Vector3.Distance(segmentStart, segmentEnd);
                float segmentTime = (segmentDistance / totalDistance) * totalTime;

                UpdateSpriteFacing(spriteRenderer, segmentStart, segmentEnd);

                float segmentElapsed = 0f;
                while (segmentElapsed < segmentTime)
                {
                    segmentElapsed += Time.deltaTime;
                    totalElapsed += Time.deltaTime;
                    float smoothT = Mathf.SmoothStep(0f, 1f, segmentElapsed / segmentTime);
                    movingUnit.transform.position = Vector3.Lerp(segmentStart, segmentEnd, smoothT);

                    if (shouldMoveCamera)
                    {
                        float totalProgress = Mathf.Clamp01(totalElapsed / totalTime);
                        cameraController.transform.position = Vector3.Lerp(
                            cameraStartPos, cameraTargetPos, Mathf.SmoothStep(0f, 1f, totalProgress));
                    }

                    yield return null;
                }

                currentPos = segmentEnd;

                if (waypoint.HasActiveEffects)
                    yield return StartCoroutine(waypoint.TriggerOnEnterEffects(movingUnit));
            }

            movingUnit.transform.position = waypoints[waypoints.Count - 1].transform.position;
            if (shouldMoveCamera)
                cameraController.transform.position = cameraTargetPos;
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

        private static void UpdateSpriteFacing(SpriteRenderer sr, Vector3 from, Vector3 to)
        {
            if (sr == null) return;
            Vector3 dir = (to - from).normalized;
            // Source art faces left — flip when moving right, not left.
            if (Mathf.Abs(dir.x) > 0.1f)
                sr.flipX = dir.x > 0;
        }

        #endregion
    }
}