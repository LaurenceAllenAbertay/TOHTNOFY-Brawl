using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Single manager for the entire overworld party — replaces the old
    /// OverworldPlayerController + OverworldFollowerController pair.
    ///
    /// ── Responsibilities ─────────────────────────────────────────────────────
    /// • Reads movement input from OverworldInputHandler and moves the leader.
    /// • Moves each follower toward a point followDistance behind the unit
    ///   directly ahead of it using Vector3.MoveTowards — no state machine,
    ///   no dispersal, no SmoothDamp overshoot.
    /// • Drives PlayMove / PlayIdle and sprite facing for every party member
    ///   using per-unit position delta computed each frame.
    ///
    /// ── Follower behaviour ────────────────────────────────────────────────────
    /// Each follower moves when their distance to the unit ahead exceeds
    /// followDistance, and stops naturally once they close the gap. When the
    /// leader stops, the follow targets become stationary and all followers
    /// converge and stop within a frame or two — no explicit freeze needed.
    ///
    /// ── Setup ────────────────────────────────────────────────────────────────
    /// Add this component to the scene manager GameObject alongside
    /// DebugOverworldSpawner. DebugOverworldSpawner calls Initialise() after
    /// instantiating all party prefabs, passing them in leader-first order.
    /// </summary>
    public class OverworldPartyManager : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Leader Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("Follower Movement")]
        [Tooltip("Target gap each follower maintains behind the unit directly ahead.")]
        [SerializeField] private float followDistance = 1.2f;

        [Tooltip("Speed at which followers close the gap when further than followDistance. " +
                 "Should be greater than moveSpeed so followers can catch up.")]
        [SerializeField] private float followSpeed = 7f;

        // ── Runtime party data ────────────────────────────────────────────────

        private readonly List<Transform>      _transforms = new List<Transform>();
        private readonly List<UnitAnimator>   _animators  = new List<UnitAnimator>();
        private readonly List<SpriteRenderer> _renderers  = new List<SpriteRenderer>();

        // Positions captured at the start of each frame, before movement.
        // Used to compute per-unit delta for animation and facing decisions.
        private Vector3[] _prevPositions = new Vector3[0];

        // ── Input ─────────────────────────────────────────────────────────────

        private Vector2 _moveInput;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Transform of the party leader. Null until Initialise() is called.
        /// Used by DebugOverworldSpawner to point OverworldCameraController at
        /// the correct target after spawning.
        /// </summary>
        public Transform LeaderTransform => _transforms.Count > 0 ? _transforms[0] : null;

        /// <summary>
        /// Registers the party in leader-first order and starts the manager.
        /// Called by DebugOverworldSpawner immediately after all prefabs are instantiated.
        /// </summary>
        public void Initialise(List<GameObject> partyObjects)
        {
            _transforms.Clear();
            _animators.Clear();
            _renderers.Clear();

            foreach (var go in partyObjects)
            {
                if (go == null) continue;
                _transforms.Add(go.transform);
                _animators.Add(go.GetComponentInChildren<UnitAnimator>());
                _renderers.Add(go.GetComponentInChildren<SpriteRenderer>());
            }

            _prevPositions = new Vector3[_transforms.Count];
            for (int i = 0; i < _transforms.Count; i++)
                _prevPositions[i] = _transforms[i].position;

            // UnitAnimator.Awake() calls SetInactiveTurn() (half speed).
            // Override that so all overworld units animate at full authored speed.
            foreach (var anim in _animators)
                anim?.SetActiveTurn();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnEnable()
        {
            OverworldInputHandler.OnMoveInput += HandleMoveInput;
        }

        private void OnDisable()
        {
            OverworldInputHandler.OnMoveInput -= HandleMoveInput;
            _moveInput = Vector2.zero;
        }

        private void Update()
        {
            if (_transforms.Count == 0) return;

            // Snapshot positions BEFORE movement so the delta used for
            // animation and facing reflects exactly what happened this frame.
            for (int i = 0; i < _transforms.Count; i++)
                _prevPositions[i] = _transforms[i].position;

            MoveLeader();
            MoveFollowers();
            UpdateAnimationsAndFacing();
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void HandleMoveInput(Vector2 input)
        {
            // Normalise only when magnitude exceeds 1 so analogue sticks get
            // natural deadzones while digital input passes through untouched.
            _moveInput = input.magnitude > 1f ? input.normalized : input;
        }

        // ── Movement ──────────────────────────────────────────────────────────

        private void MoveLeader()
        {
            if (_moveInput.sqrMagnitude < 0.01f) return;

            // Input is XY (2D pad), mapped to XZ world movement.
            _transforms[0].position +=
                new Vector3(_moveInput.x, 0f, _moveInput.y) * (moveSpeed * Time.deltaTime);
        }

        private void MoveFollowers()
        {
            for (int i = 1; i < _transforms.Count; i++)
            {
                Transform self  = _transforms[i];
                Transform ahead = _transforms[i - 1]; // unit directly ahead in chain

                Vector3 toAhead  = ahead.position - self.position;
                float   distance = toAhead.magnitude;

                // Already at or within followDistance — nothing to do.
                if (distance <= followDistance) continue;

                // Target: the point exactly followDistance behind the unit ahead.
                // MoveTowards never overshoots, so spacing stays consistent.
                Vector3 targetPos = ahead.position - toAhead.normalized * followDistance;
                self.position = Vector3.MoveTowards(self.position, targetPos,
                                                    followSpeed * Time.deltaTime);
            }
        }

        // ── Animation & Facing ────────────────────────────────────────────────

        private void UpdateAnimationsAndFacing()
        {
            for (int i = 0; i < _transforms.Count; i++)
            {
                Vector3 delta    = _transforms[i].position - _prevPositions[i];
                bool    isMoving = delta.sqrMagnitude > 1e-6f;

                if (isMoving)
                    _animators[i]?.PlayMove();
                else
                    _animators[i]?.PlayIdle();

                // Update facing only when there is meaningful horizontal movement,
                // so the sprite doesn't snap when moving purely on Z.
                // flipX = true  → sprite faces right (matching combat convention:
                //                  unflipped = facing left, flipped = facing right).
                if (_renderers[i] != null && isMoving && Mathf.Abs(delta.x) > 0.001f)
                    _renderers[i].flipX = delta.x > 0f;
            }
        }
    }
}