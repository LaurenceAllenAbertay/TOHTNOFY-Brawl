using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Drives the lead overworld character with real-time 8-directional movement.
    /// Reads input from OverworldInputHandler and moves the character in world space.
    /// Drives Idle/Move states on UnitAnimator and handles sprite facing.
    /// </summary>
    public class OverworldPlayerController : MonoBehaviour
    {
        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("References")]
        [SerializeField] private UnitAnimator unitAnimator;
        [SerializeField] private SpriteRenderer spriteRenderer;

        // ── Public API ────────────────────────────────────────────────────────

        public bool IsMoving { get; private set; }

        // ── Private ───────────────────────────────────────────────────────────

        private Vector2 currentInput;

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
            if (unitAnimator != null)
                unitAnimator.SetAnimationSpeed(1f);
        }

        private void OnEnable()
        {
            OverworldInputHandler.OnMoveInput += HandleMoveInput;
        }

        private void OnDisable()
        {
            OverworldInputHandler.OnMoveInput -= HandleMoveInput;
        }

        private void Update()
        {
            ApplyMovement();
            UpdateAnimation();
            UpdateSpriteFacing();
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void HandleMoveInput(Vector2 input)
        {
            currentInput = input.magnitude > 1f ? input.normalized : input;
        }

        // ── Movement ──────────────────────────────────────────────────────────

        private void ApplyMovement()
        {
            if (currentInput.sqrMagnitude < 0.01f)
            {
                IsMoving = false;
                return;
            }

            Vector3 worldMove = new Vector3(currentInput.x, 0f, currentInput.y);
            transform.position += worldMove * (moveSpeed * Time.deltaTime);
            IsMoving = true;
        }

        // ── Animation & Facing ────────────────────────────────────────────────

        private void UpdateAnimation()
        {
            if (unitAnimator == null) return;

            if (IsMoving)
                unitAnimator.PlayMove();
            else
                unitAnimator.PlayIdle();
        }

        private void UpdateSpriteFacing()
        {
            if (spriteRenderer == null || !IsMoving) return;

            if (Mathf.Abs(currentInput.x) > 0.1f)
                spriteRenderer.flipX = currentInput.x < 0f;
        }
    }
}