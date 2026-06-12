using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Follows whoever is directly in front of it in the party chain.
    ///
    /// ── Chaining ─────────────────────────────────────────────────────────────
    /// Rather than all followers reading from a shared history trail, each
    /// follower simply follows the Transform directly in front of them:
    ///   Follower 1 → follows the leader's Transform
    ///   Follower 2 → follows Follower 1's Transform
    /// This naturally produces correct spacing with no trail maths needed.
    ///
    /// Assign 'followTarget' to the Transform of whoever is directly ahead in
    /// the chain — the leader for follower 1, follower 1 for follower 2, etc.
    ///
    /// ── States ───────────────────────────────────────────────────────────────
    /// FOLLOWING  — target is moving, stay followDistance behind their position.
    /// DISPERSING — target stopped, walk to a random spot near the target.
    /// IDLE       — arrived at idle spot, stand still.
    /// REJOINING  — target started moving, close the gap before resuming follow.
    /// </summary>
    public class OverworldFollowerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Chain")]
        [Tooltip("The Transform to follow — the leader for follower 1, " +
                 "the previous follower's Transform for follower 2+.")]
        [SerializeField] private Transform followTarget;

        [Tooltip("Reference to the lead OverworldPlayerController so all " +
                 "followers can read IsMoving from the same source.")]
        [SerializeField] private OverworldPlayerController leader;

        [Tooltip("How far behind followTarget this follower tries to stay.")]
        [SerializeField] private float followDistance = 1.2f;

        [Tooltip("Movement speed while following.")]
        [SerializeField] private float followSpeed = 6f;

        [Header("Idle Dispersal")]
        [Tooltip("Maximum radius around followTarget to pick an idle spot.")]
        [SerializeField] private float idleSpreadRadius = 1.5f;

        [Tooltip("Minimum radius — prevents clustering on top of the target.")]
        [SerializeField] private float idleSpreadMinRadius = 0.6f;

        [Tooltip("SmoothDamp time for dispersal and rejoin transitions.")]
        [SerializeField] private float smoothTime = 0.25f;

        [Tooltip("Distance at which the follower is considered arrived.")]
        [SerializeField] private float arrivalThreshold = 0.1f;

        [Header("References")]
        [SerializeField] private UnitAnimator unitAnimator;
        [SerializeField] private SpriteRenderer spriteRenderer;

        // ── State ─────────────────────────────────────────────────────────────

        private enum FollowerState { Following, Dispersing, Idle, Rejoining }
        private FollowerState state = FollowerState.Following;

        private Vector3 idleTarget;
        private Vector3 smoothVelocity;
        private Vector3 previousPosition;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void Awake()
        {
            if (unitAnimator == null)
                unitAnimator = GetComponentInChildren<UnitAnimator>();

            if (spriteRenderer == null)
                spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void Start()
        {
            previousPosition = transform.position;

            if (unitAnimator != null)
                unitAnimator.SetAnimationSpeed(1f);
        }

        private void Update()
        {
            if (followTarget == null || leader == null) return;

            UpdateState();
            ExecuteState();
            UpdateAnimation();
            UpdateSpriteFacing();

            previousPosition = transform.position;
        }

        // ── State Transitions ─────────────────────────────────────────────────

        private void UpdateState()
        {
            bool leaderMoving = leader.IsMoving;

            switch (state)
            {
                case FollowerState.Following:
                    if (!leaderMoving)
                    {
                        idleTarget = PickIdleSpot();
                        smoothVelocity = Vector3.zero;
                        state = FollowerState.Dispersing;
                    }
                    break;

                case FollowerState.Dispersing:
                    if (leaderMoving)
                    {
                        smoothVelocity = Vector3.zero;
                        state = FollowerState.Rejoining;
                        break;
                    }
                    if (Vector3.Distance(transform.position, idleTarget) < arrivalThreshold)
                        state = FollowerState.Idle;
                    break;

                case FollowerState.Idle:
                    if (leaderMoving)
                    {
                        smoothVelocity = Vector3.zero;
                        state = FollowerState.Rejoining;
                    }
                    break;

                case FollowerState.Rejoining:
                    if (!leaderMoving)
                    {
                        idleTarget = PickIdleSpot();
                        smoothVelocity = Vector3.zero;
                        state = FollowerState.Dispersing;
                        break;
                    }
                    if (Vector3.Distance(transform.position, GetFollowTarget()) < arrivalThreshold)
                    {
                        smoothVelocity = Vector3.zero;
                        state = FollowerState.Following;
                    }
                    break;
            }
        }

        // ── State Execution ───────────────────────────────────────────────────

        private void ExecuteState()
        {
            switch (state)
            {
                case FollowerState.Following:
                    // Stay followDistance behind the target
                    Vector3 followPos = GetFollowTarget();
                    if (Vector3.Distance(transform.position, followPos) > arrivalThreshold)
                        transform.position = Vector3.MoveTowards(
                            transform.position, followPos, followSpeed * Time.deltaTime);
                    break;

                case FollowerState.Dispersing:
                    transform.position = Vector3.MoveTowards(
                        transform.position,
                        idleTarget,
                        followSpeed * Time.deltaTime);
                    break;

                case FollowerState.Rejoining:
                    transform.position = Vector3.SmoothDamp(
                        transform.position,
                        GetFollowTarget(),
                        ref smoothVelocity,
                        smoothTime);
                    break;

                case FollowerState.Idle:
                    break;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the point followDistance behind the followTarget's current position.
        /// </summary>
        private Vector3 GetFollowTarget()
        {
            // Direction from this follower toward the target
            Vector3 toTarget = followTarget.position - transform.position;

            // If we're already further than followDistance, move toward a point
            // that is followDistance behind the target.
            if (toTarget.magnitude > followDistance)
            {
                Vector3 dir = toTarget.normalized;
                return followTarget.position - dir * followDistance;
            }

            // Already within range — stay put
            return transform.position;
        }

        /// <summary>
        /// Picks a random idle spot at a random angle and radius around followTarget.
        /// </summary>
        private Vector3 PickIdleSpot()
        {
            float angle    = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(idleSpreadMinRadius, idleSpreadRadius);

            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * distance,
                0f,
                Mathf.Sin(angle) * distance);

            Vector3 candidate = followTarget.position + offset;

            // If the candidate is in front of the leader depth-wise, it must be
            // offset far enough on X to not visually overlap with them.
            // The minimum side clearance is idleSpreadMinRadius.
            if (candidate.z < leader.transform.position.z)
            {
                float xDiff = candidate.x - leader.transform.position.x;
                if (Mathf.Abs(xDiff) < idleSpreadMinRadius)
                {
                    // Nudge X outward in whichever direction it's already leaning.
                    // If xDiff is zero pick a side randomly.
                    float sign = xDiff >= 0f ? 1f : -1f;
                    candidate.x = leader.transform.position.x + sign * idleSpreadMinRadius;
                }
            }

            return candidate;
        }

        // ── Animation & Facing ────────────────────────────────────────────────

        private void UpdateAnimation()
        {
            if (unitAnimator == null) return;

            Vector3 moveDelta = transform.position - previousPosition;
            bool isMoving = moveDelta.magnitude > 0.001f;

            if (isMoving) unitAnimator.PlayMove();
            else          unitAnimator.PlayIdle();
        }

        private void UpdateSpriteFacing()
        {
            if (spriteRenderer == null) return;

            Vector3 moveDelta = transform.position - previousPosition;
            if (Mathf.Abs(moveDelta.x) > 0.001f)
                spriteRenderer.flipX = moveDelta.x < 0f;
        }
    }
}