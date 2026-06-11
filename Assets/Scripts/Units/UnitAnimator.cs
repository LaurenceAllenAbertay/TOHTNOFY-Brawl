using UnityEngine;
using System.Collections;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(Animator))]
    public class UnitAnimator : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private bool playIdleOnStart = true;
        [SerializeField] private float activeTurnSpeed = 0.65f;
        [SerializeField] private float inactiveTurnSpeed = 0.3f;

        private Animator animator;
        private string currentAnimation;

        // Knockback state tracking
        private bool isInKnockbackSequence = false;
        private Coroutine knockbackSequence;

        // Animation state names (these should match your Animator states)
        private const string IDLE_STATE = "Idle";
        private const string MOVE_STATE = "Move";
        private const string ATTACK_STATE = "Recoil_Shot";
        private const string HURT_STATE = "Hurt";
        private const string DEATH_STATE = "Death";
        private const string KNOCKBACK_START_STATE = "Knockback_Start";
        private const string KNOCKBACK_END_STATE = "Knockback_End";

        // Animation state tracking
        public bool IsAnimating => animator != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f;
        public bool IsInKnockbackSequence => isInKnockbackSequence;
        public string CurrentAnimation => currentAnimation;

        void Awake()
        {
            animator = GetComponent<Animator>();

            if (animator == null)
            {
                Debug.LogError($"UnitAnimator on {gameObject.name} requires an Animator component!");
                return;
            }

            SetInactiveTurn();
        }

        void Start()
        {
            if (playIdleOnStart)
            {
                PlayIdle();
            }
        }

        #region Animation Events

        /// <summary>
        /// Fired when the cast effect Animation Event is hit on a clip.
        /// Subscribe in Unit before playing the animation; unsubscribe after.
        /// </summary>
        public event System.Action OnCastEffectEvent;

        /// <summary>
        /// Called by Unity Animation Events on clips — name must match exactly in the Animation window.
        /// Add an Animation Event named "AnimEvent_CastEffect" at the desired frame of each ability clip.
        /// </summary>
        public void AnimEvent_CastEffect() => OnCastEffectEvent?.Invoke();

        #endregion

        #region Public Animation Methods

        /// <summary>
        /// Play the idle animation (loops continuously)
        /// </summary>
        public void PlayIdle()
        {
            if (!isInKnockbackSequence) // Don't interrupt knockback sequences
                PlayAnimation(IDLE_STATE, true);
        }

        /// <summary>
        /// Play the walk animation (loops continuously)
        /// </summary>
        public void PlayMove()
        {
            if (!isInKnockbackSequence) // Don't interrupt knockback sequences
                PlayAnimation(MOVE_STATE, true);
        }

        /// <summary>
        /// Play the attack animation (plays once, then returns to idle)
        /// </summary>
        public void PlayAttack()
        {
            if (isInKnockbackSequence) return; // Don't interrupt knockback sequences

            PlayAnimation(ATTACK_STATE, false);

            // Return to idle after attack finishes
            StartCoroutine(ReturnToIdleAfterAnimation(ATTACK_STATE));
        }

        /// <summary>
        /// Play a custom animation state (e.g., "Attack_Ranged_1", "Attack_Melee_2")
        /// </summary>
        public void PlayAnimation(string animationState)
        {
            if (string.IsNullOrEmpty(animationState)) return;
            if (isInKnockbackSequence) return; // Don't interrupt knockback sequences

            PlayAnimation(animationState, false);

            // Return to idle after animation finishes
            StartCoroutine(ReturnToIdleAfterAnimation(animationState));
        }

        /// <summary>
        /// Play the hurt animation (plays once, then returns to idle)
        /// </summary>
        public void PlayHurt()
        {
            if (isInKnockbackSequence) return; // Don't interrupt knockback sequences

            PlayAnimation(HURT_STATE, false);

            // Return to idle after hurt finishes
            StartCoroutine(ReturnToIdleAfterAnimation(HURT_STATE));
        }

        /// <summary>
        /// Play the death animation (plays once and stays on last frame).
        /// Forces animator speed to 1 so the animation plays at full speed regardless
        /// of whether this unit was at inactive-turn speed when it died.
        /// </summary>
        public void PlayDeath()
        {
            // Death can interrupt knockback
            if (knockbackSequence != null)
            {
                StopCoroutine(knockbackSequence);
                isInKnockbackSequence = false;
            }

            // Always play death at full speed — the unit may be at inactiveTurnSpeed.
            if (animator != null)
                animator.speed = 1f;

            PlayAnimation(DEATH_STATE, false);
        }

        /// <summary>
        /// Force a specific animation to play immediately
        /// </summary>
        public void ForcePlayAnimation(string stateName)
        {
            if (animator != null)
            {
                animator.Play(stateName, 0, 0f); // Layer 0, start from beginning
                currentAnimation = stateName;
            }
        }

        #endregion

        #region Knockback Animation Methods

        /// <summary>
        /// Start the knockback animation sequence - plays Knockback_Start
        /// For multi-tile knockbacks, the animation will naturally hold at the end frame
        /// </summary>
        public void PlayKnockbackStart()
        {
            // Stop any existing knockback sequence
            if (knockbackSequence != null)
            {
                StopCoroutine(knockbackSequence);
            }

            isInKnockbackSequence = true;

            // Force play the animation to ensure it starts immediately
            ForcePlayAnimation(KNOCKBACK_START_STATE);

            // Don't clamp with speed=0, let the animation controller handle it
        }

        /// <summary>
        /// End the knockback sequence - plays Knockback_End and returns to idle
        /// </summary>
        public void PlayKnockbackEnd()
        {
            // Make sure animator speed is restored
            SetActiveTurn(); // Or SetInactiveTurn() based on current turn

            // Play end animation
            PlayAnimation(KNOCKBACK_END_STATE, false);

            // Start sequence to return to idle after end - this will now also handle facing
            knockbackSequence = StartCoroutine(ReturnToIdleAfterKnockbackEnd());
        }

        /// <summary>
        /// Cancel knockback sequence and return to idle immediately
        /// </summary>
        public void CancelKnockback()
        {
            if (knockbackSequence != null)
            {
                StopCoroutine(knockbackSequence);
                knockbackSequence = null;
            }

            isInKnockbackSequence = false;
            PlayIdle();
        }

        /// <summary>
        /// Play a single-tile knockback (start and end quickly)
        /// </summary>
        public void PlaySingleKnockback()
        {
            knockbackSequence = StartCoroutine(SingleKnockbackSequence());
        }

        #endregion

        #region Turn Speed Control

        /// <summary>
        /// Set animation speed for when it's this unit's turn
        /// </summary>
        public void SetActiveTurn()
        {
            if (animator != null)
            {
                SetAnimationSpeed(activeTurnSpeed);
            }
        }

        /// <summary>
        /// Set animation speed for when it's NOT this unit's turn
        /// </summary>
        public void SetInactiveTurn()
        {
            if (animator != null)
            {
                SetAnimationSpeed(inactiveTurnSpeed);
            }
        }

        /// <summary>
        /// Set custom animation speed
        /// </summary>
        public void SetAnimationSpeed(float speed)
        {
            if (animator != null)
            {
                animator.speed = speed;
            }
        }

        #endregion

        #region Private Methods

        private void PlayAnimation(string stateName, bool canInterrupt)
        {
            if (animator == null) return;

            // Validate that the state exists
            if (!HasState(stateName))
            {
                return;
            }

            // Don't interrupt death animation
            if (currentAnimation == DEATH_STATE && stateName != DEATH_STATE) return;

            // Don't interrupt knockback sequences (except for death)
            if (isInKnockbackSequence && stateName != DEATH_STATE &&
                stateName != KNOCKBACK_START_STATE && stateName != KNOCKBACK_END_STATE) return;

            // Don't play the same animation twice unless it's interruptible
            if (!canInterrupt && currentAnimation == stateName) return;

            animator.Play(stateName);

            currentAnimation = stateName;
        }

        public bool HasState(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return false;

            // Check if the state exists in layer 0
            return animator.HasState(0, Animator.StringToHash(stateName));
        }

        private System.Collections.IEnumerator ReturnToIdleAfterAnimation(string animationState)
        {
            // Wait for the animation to finish
            yield return new WaitForSeconds(0.1f); // Small delay to ensure animation started

            while (animator.GetCurrentAnimatorStateInfo(0).IsName(animationState) &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
            {
                yield return null;
            }

            // Return to idle if we're not dead and not in a knockback sequence
            if (currentAnimation != DEATH_STATE && !isInKnockbackSequence)
            {
                PlayIdle();
            }
        }

        #region Knockback Animation Coroutines

        private IEnumerator ReturnToIdleAfterKnockbackEnd()
        {
            // Wait for Knockback_End to complete
            yield return new WaitForSeconds(0.1f); // Ensure animation started

            while (animator.GetCurrentAnimatorStateInfo(0).IsName(KNOCKBACK_END_STATE) &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.95f)
            {
                yield return null;
            }

            // End the knockback sequence
            isInKnockbackSequence = false;
            knockbackSequence = null;

            // Return to idle
            if (currentAnimation != DEATH_STATE)
            {
                PlayIdle();
            }
        }

        private IEnumerator SingleKnockbackSequence()
        {
            isInKnockbackSequence = true;

            // Play start animation
            PlayAnimation(KNOCKBACK_START_STATE, false);

            // Wait for start to reach about 70% (don't wait for full completion)
            yield return new WaitForSeconds(0.2f);

            // Quickly transition to end animation
            PlayAnimation(KNOCKBACK_END_STATE, false);

            // Wait for end to complete
            yield return new WaitForSeconds(0.3f);

            // Return to idle
            isInKnockbackSequence = false;
            knockbackSequence = null;

            if (currentAnimation != DEATH_STATE)
            {
                PlayIdle();
            }
        }

        #endregion

        #endregion

        #region Utility Methods

        /// <summary>
        /// Check if a specific animation is currently playing
        /// </summary>
        public bool IsPlayingAnimation(string stateName)
        {
            if (animator == null) return false;
            return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
        }

        /// <summary>
        /// Get the normalized time of the current animation (0 = start, 1 = end)
        /// </summary>
        public float GetCurrentAnimationTime()
        {
            if (animator == null) return 0f;
            return animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        }

        /// <summary>
        /// Set animator parameter (for more complex state machines if needed later)
        /// </summary>
        public void SetBool(string parameterName, bool value)
        {
            if (animator != null)
                animator.SetBool(parameterName, value);
        }

        public void SetTrigger(string parameterName)
        {
            if (animator != null)
                animator.SetTrigger(parameterName);
        }

        public void SetFloat(string parameterName, float value)
        {
            if (animator != null)
                animator.SetFloat(parameterName, value);
        }

        #endregion

        #region Debug Methods

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugPlayAnimation(string animName)
        {
            switch (animName.ToLower())
            {
                case "idle": PlayIdle(); break;
                case "walk": PlayMove(); break;
                case "attack": PlayAttack(); break;
                case "hurt": PlayHurt(); break;
                case "death": PlayDeath(); break;
                case "knockback_start": PlayKnockbackStart(); break;
                case "knockback_end": PlayKnockbackEnd(); break;
                case "single_knockback": PlaySingleKnockback(); break;
                default: ForcePlayAnimation(animName); break;
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugTestKnockbackSequence()
        {
            StartCoroutine(TestKnockbackSequence());
        }

        private IEnumerator TestKnockbackSequence()
        {
            Debug.Log("Testing 3-tile knockback sequence");

            PlayKnockbackStart();
            yield return new WaitForSeconds(2f); // Simulate multi-tile movement time

            PlayKnockbackEnd();
            yield return new WaitForSeconds(1f);

            Debug.Log("Test knockback sequence complete");
        }

        #endregion

        #region Editor Context Menu

#if UNITY_EDITOR
        [UnityEngine.ContextMenu("Test Idle")]
        private void TestIdle() => PlayIdle();

        [UnityEngine.ContextMenu("Test Move")]
        private void TestMove() => PlayMove();

        [UnityEngine.ContextMenu("Test Attack")]
        private void TestAttack() => PlayAttack();

        [UnityEngine.ContextMenu("Test Hurt")]
        private void TestHurt() => PlayHurt();

        [UnityEngine.ContextMenu("Test Death")]
        private void TestDeath() => PlayDeath();

        [UnityEngine.ContextMenu("Test Knockback Start")]
        private void TestKnockbackStart() => PlayKnockbackStart();

        [UnityEngine.ContextMenu("Test Knockback End")]
        private void TestKnockbackEnd() => PlayKnockbackEnd();

        [UnityEngine.ContextMenu("Test Single Knockback")]
        private void TestSingleKnockback() => PlaySingleKnockback();

        [UnityEngine.ContextMenu("Test Multi-Tile Knockback")]
        private void TestMultiTileKnockback() => DebugTestKnockbackSequence();

        [UnityEngine.ContextMenu("Cancel Knockback")]
        private void TestCancelKnockback() => CancelKnockback();

        [UnityEngine.ContextMenu("Force Play Current Clip")]
        private void ForcePlayCurrentClip()
        {
            if (animator != null)
            {
                var currentState = animator.GetCurrentAnimatorStateInfo(0);
                animator.Play(currentState.fullPathHash, 0, 0f);
            }
        }
#endif

        #endregion
    }
}