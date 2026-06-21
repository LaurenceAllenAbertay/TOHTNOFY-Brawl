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
        private Unit     _unit;
        private string   currentAnimation;

        // Knockback state tracking
        private bool isInKnockbackSequence = false;
        private Coroutine knockbackSequence;

        // ── Idle tier state ───────────────────────────────────────────────────

        private bool _isTalking      = false;
        private bool _isHurtIdle     = false;   // unit HP < 25 %
        private bool _isBadIdle      = false;   // 2+ bodies AND outnumbered

        // ── Animation state names ─────────────────────────────────────────────

        // Contextual idle states — checked with HasState() so missing states
        // fall back gracefully to Idle_Good / Idle_Good_Talking.
        private const string IDLE_GOOD          = "Idle_Good";
        private const string IDLE_GOOD_TALKING  = "Idle_Good_Talking";
        private const string IDLE_BAD           = "Idle_Bad";
        private const string IDLE_BAD_TALKING   = "Idle_Bad_Talking";
        private const string IDLE_HURT          = "Idle_Hurt";
        private const string IDLE_HURT_TALKING  = "Idle_Hurt_Talking";

        private const string MOVE_STATE           = "Move";
        private const string ATTACK_STATE         = "Recoil_Shot";
        private const string HURT_STATE           = "Hurt";
        private const string DEATH_STATE          = "Death";
        private const string KNOCKBACK_START_STATE = "Knockback_Start";
        private const string KNOCKBACK_END_STATE   = "Knockback_End";

        // Animation state tracking
        public bool IsAnimating => animator != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f;
        public bool IsInKnockbackSequence => isInKnockbackSequence;
        public string CurrentAnimation => currentAnimation;
        public bool IsHurtIdle => _isHurtIdle;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            animator = GetComponent<Animator>();
            _unit    = GetComponentInParent<Unit>();

            if (animator == null)
            {
                Debug.LogError($"UnitAnimator on {gameObject.name} requires an Animator component!");
                return;
            }

            SetInactiveTurn();
        }

        void Start()
        {
            // Evaluate the initial idle tier before subscribing so the first
            // PlayIdle() call uses the correct state.
            EvaluateIdleTier();

            if (playIdleOnStart)
                PlayIdle();

            SubscribeToEvents();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        // ── Event subscriptions ───────────────────────────────────────────────

        private void SubscribeToEvents()
        {
            Unit.OnHealthChanged              += HandleHealthChanged;
            UnitManager.OnUnitDied            += HandleRosterChanged;
            UnitManager.OnBodySpawned         += HandleRosterChanged;
            UnitManager.OnUnitUnregistered    += HandleRosterChanged;
            DialogueBubbleUI.OnDialogueStarted += HandleDialogueStarted;
            DialogueBubbleUI.OnDialogueEnded   += HandleDialogueEnded;
        }

        private void UnsubscribeFromEvents()
        {
            Unit.OnHealthChanged              -= HandleHealthChanged;
            UnitManager.OnUnitDied            -= HandleRosterChanged;
            UnitManager.OnBodySpawned         -= HandleRosterChanged;
            UnitManager.OnUnitUnregistered    -= HandleRosterChanged;
            DialogueBubbleUI.OnDialogueStarted -= HandleDialogueStarted;
            DialogueBubbleUI.OnDialogueEnded   -= HandleDialogueEnded;
        }

        // ── Idle tier evaluation ──────────────────────────────────────────────

        /// <summary>
        /// Re-evaluates which idle tier this unit belongs to and refreshes the
        /// idle animation if the unit is currently idling.
        /// </summary>
        private void EvaluateIdleTier()
        {
            if (_unit == null) return;

            bool wasHurt = _isHurtIdle;
            bool wasBad  = _isBadIdle;

            // Hurt check — below 25 % max health.
            int maxHp = _unit.characterData != null ? _unit.characterData.maxHealth : 0;
            _isHurtIdle = maxHp > 0 && _unit.currentHealth < maxHp * 0.25f;

            // Bad check — 2+ player bodies AND remaining players < remaining enemies.
            int bodyCount     = UnitManager.AllBodies.Count;
            int playerCount   = UnitManager.PlayerUnits.Count;
            int enemyCount    = UnitManager.EnemyUnits.Count;
            _isBadIdle = bodyCount >= 2 && playerCount < enemyCount;

            // Only refresh idle if the tier actually changed and the unit is currently
            // playing some variety of idle (so we don't interrupt attacks, etc.).
            if ((_isHurtIdle != wasHurt || _isBadIdle != wasBad) && IsCurrentlyIdling())
                PlayIdle();
        }

        /// <summary>Returns true when the currently playing animation is any idle variant.</summary>
        private bool IsCurrentlyIdling()
        {
            return currentAnimation == IDLE_GOOD         ||
                   currentAnimation == IDLE_GOOD_TALKING ||
                   currentAnimation == IDLE_BAD          ||
                   currentAnimation == IDLE_BAD_TALKING  ||
                   currentAnimation == IDLE_HURT         ||
                   currentAnimation == IDLE_HURT_TALKING;
        }

        /// <summary>
        /// Returns the correct idle state name for the current tier and talking flag,
        /// falling back to Idle_Good / Idle_Good_Talking when the specific state is absent.
        /// </summary>
        private string ResolveIdleState(bool talking)
        {
            if (_isHurtIdle)
            {
                string s = talking ? IDLE_HURT_TALKING : IDLE_HURT;
                if (HasState(s)) return s;
            }
            else if (_isBadIdle)
            {
                string s = talking ? IDLE_BAD_TALKING : IDLE_BAD;
                if (HasState(s)) return s;
            }

            // Good tier — always present.
            return talking ? IDLE_GOOD_TALKING : IDLE_GOOD;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void HandleHealthChanged(Unit changed)
        {
            if (changed != _unit) return;
            EvaluateIdleTier();
        }

        private void HandleRosterChanged(Unit _)
        {
            // Any change to the live/body roster may flip the Bad condition.
            EvaluateIdleTier();
        }

        private void HandleDialogueStarted(Unit speaker)
        {
            if (speaker != _unit) return;
            _isTalking = true;

            // Only swap if we're currently idling — don't interrupt an attack.
            if (!IsCurrentlyIdling()) return;

            // Swap to the talking variant at the exact normalised time we're at
            // so the animation continues seamlessly from the same frame.
            float normalizedTime = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            string talkingState  = ResolveIdleState(talking: true);
            PlayIdleAtTime(talkingState, normalizedTime);
        }

        private void HandleDialogueEnded(Unit speaker)
        {
            if (speaker != _unit) return;
            _isTalking = false;

            if (!IsCurrentlyIdling()) return;

            float normalizedTime  = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            string nonTalkState   = ResolveIdleState(talking: false);
            PlayIdleAtTime(nonTalkState, normalizedTime);
        }

        // ── Private idle helpers ──────────────────────────────────────────────

        /// <summary>
        /// Plays an idle state at a specific normalised time so the sprite sheet
        /// continues from the same frame rather than restarting.
        /// Bypasses the canInterrupt guard intentionally — idle↔talking swaps always apply.
        /// </summary>
        private void PlayIdleAtTime(string stateName, float normalizedTime)
        {
            if (animator == null) return;
            if (!HasState(stateName)) return;
            if (currentAnimation == DEATH_STATE) return;

            // Use frac to keep the time within [0, 1) for a looping clip.
            float frac = normalizedTime % 1f;
            animator.Play(stateName, 0, frac);
            currentAnimation = stateName;
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

        /// <summary>
        /// Fired mid-animation to trigger one or more ability effects by slot index.
        /// AbilitySequencer subscribes to this when suppressCameraTransitions is true and
        /// dispatches every AbilityEffect whose midAnimationEventIndex matches the slot fired.
        /// </summary>
        public event System.Action<int> OnAbilityEffectEvent;

        /// <summary>
        /// Called by Unity Animation Events on ability clips.
        /// Place an Animation Event named "AnimEvent_AbilityEffect0" at the frame where
        /// slot-0 effects should fire, "AnimEvent_AbilityEffect1" for slot 1, and so on.
        /// The integer maps to AbilityEffect.midAnimationEventIndex on each effect asset.
        /// Only add as many slots as your animations actually need — four is the supported maximum.
        /// </summary>
        public void AnimEvent_AbilityEffect0() => OnAbilityEffectEvent?.Invoke(0);
        public void AnimEvent_AbilityEffect1() => OnAbilityEffectEvent?.Invoke(1);
        public void AnimEvent_AbilityEffect2() => OnAbilityEffectEvent?.Invoke(2);
        public void AnimEvent_AbilityEffect3() => OnAbilityEffectEvent?.Invoke(3);

        #endregion

        #region Public Animation Methods

        /// <summary>
        /// Plays the contextually correct idle animation for this unit's current tier
        /// (Hurt → Bad → Good) and talking state.
        /// </summary>
        public void PlayIdle()
        {
            if (isInKnockbackSequence) return;
            string idleState = ResolveIdleState(_isTalking);
            PlayAnimation(idleState, true);
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
        /// Yields until the Hurt animation has finished playing.
        /// Waits up to maxWait seconds before giving up gracefully.
        /// Call this from AbilitySequencer immediately after PlayHurt() to
        /// synchronise with the health bar tween before checking for death.
        /// </summary>
        public IEnumerator WaitForHurtAnimation()
        {
            // One frame grace period for the animator to register the state change.
            yield return null;

            const float maxWait = 5f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName(HURT_STATE) && stateInfo.normalizedTime >= 1.0f && !stateInfo.loop)
                    yield break;

                // If the animator has already transitioned away (e.g. back to idle),
                // the hurt animation is considered done.
                if (!stateInfo.IsName(HURT_STATE))
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }

            Debug.LogWarning($"[UnitAnimator] Hurt animation wait timed out on {gameObject.name}.");
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