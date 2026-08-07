using UnityEngine;
using System.Collections;

namespace DDD.TNFY.BRAWL
{
    [RequireComponent(typeof(Animator))]
    public class UnitAnimator : MonoBehaviour
    {
        [Header("Animation Settings")]
        [SerializeField] private bool playIdleOnStart = true;
        [SerializeField] [Range(0f, 1f)] private float inactiveTurnSpeedMultiplier = 0.5f;

        private Animator animator;
        private Unit     _unit;
        private string   currentAnimation;
        
        private bool isInKnockbackSequence = false;
        private Coroutine knockbackSequence;

        private Coroutine holdSequence;
        private StatusEffectInstance activeHoldInstance;
        private string activeHoldReleaseState;

        private bool _isTalking      = false;
        private bool _isHurtIdle     = false;  
        private bool _isBadIdle      = false;  

        private const string IDLE_GOOD          = "Idle_Good";
        private const string IDLE_GOOD_TALKING  = "Idle_Good_Talking";
        private const string IDLE_BAD           = "Idle_Bad";
        private const string IDLE_BAD_TALKING   = "Idle_Bad_Talking";
        private const string IDLE_HURT          = "Idle_Hurt";
        private const string IDLE_HURT_TALKING  = "Idle_Hurt_Talking";

        private const string MOVE_STATE           = "Move";
        private const string MOVE_BAD_STATE       = "Move_Bad";
        private const string MOVE_HURT_STATE      = "Move_Hurt";
        private const string ATTACK_STATE         = "Recoil_Shot";
        private const string HURT_STATE           = "Hurt";
        private const string DOWNED_STATE          = "Downed";
        private const string KNOCKBACK_START_STATE = "Knockback_Start";
        private const string KNOCKBACK_END_STATE   = "Knockback_End";
        private const string TARGETING_STATE       = "Targeting";
        private const string TARGETING_BAD_STATE   = "Targeting_Bad";
        private const string TARGETING_HURT_STATE  = "Targeting_Hurt";
        private const string JUMP_STATE            = "Jump";
        private const string JUMP_BAD_STATE        = "Jump_Bad";
        private const string JUMP_HURT_STATE       = "Jump_Hurt";
        
        public bool IsAnimating => animator != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f;
        public bool IsInKnockbackSequence => isInKnockbackSequence;
        public string CurrentAnimation => currentAnimation;
        public bool IsHurtIdle => _isHurtIdle;
        
        public bool HasJumpAnimation => HasState(ResolveJumpState());

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
            EvaluateIdleTier();

            if (playIdleOnStart)
                PlayIdleAtRandomTime();

            SubscribeToEvents();
        }

        void OnDestroy()
        {
            UnsubscribeFromEvents();
        }

        private void SubscribeToEvents()
        {
            Unit.OnHealthChanged              += HandleHealthChanged;
            UnitManager.OnUnitDied            += HandleRosterChanged;
            UnitManager.OnBodySpawned         += HandleRosterChanged;
            UnitManager.OnUnitUnregistered    += HandleRosterChanged;
            DialogueBubbleUI.OnDialogueStarted += HandleDialogueStarted;
            DialogueBubbleUI.OnDialogueEnded   += HandleDialogueEnded;
            StatusEffectManager.OnStatusEffectRemoved += HandleStatusEffectRemoved;
        }

        private void UnsubscribeFromEvents()
        {
            Unit.OnHealthChanged              -= HandleHealthChanged;
            UnitManager.OnUnitDied            -= HandleRosterChanged;
            UnitManager.OnBodySpawned         -= HandleRosterChanged;
            UnitManager.OnUnitUnregistered    -= HandleRosterChanged;
            DialogueBubbleUI.OnDialogueStarted -= HandleDialogueStarted;
            DialogueBubbleUI.OnDialogueEnded   -= HandleDialogueEnded;
            StatusEffectManager.OnStatusEffectRemoved -= HandleStatusEffectRemoved;
        }

        private void EvaluateIdleTier()
        {
            if (_unit == null) return;

            if (_unit.IsDead) return;

            bool wasHurt = _isHurtIdle;
            bool wasBad  = _isBadIdle;

            int maxHp = _unit.maxHealth;
            _isHurtIdle = maxHp > 0 && _unit.currentHealth < maxHp * 0.25f;
            
            _isBadIdle = UnitManager.IsPlayerTeamInBadState;
            
            if ((_isHurtIdle != wasHurt || _isBadIdle != wasBad) && IsCurrentlyIdling())
                PlayIdle();
        }
        
        private bool IsCurrentlyIdling()
        {
            return currentAnimation == IDLE_GOOD         ||
                   currentAnimation == IDLE_GOOD_TALKING ||
                   currentAnimation == IDLE_BAD          ||
                   currentAnimation == IDLE_BAD_TALKING  ||
                   currentAnimation == IDLE_HURT         ||
                   currentAnimation == IDLE_HURT_TALKING;
        }

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
            
            return talking ? IDLE_GOOD_TALKING : IDLE_GOOD;
        }

        private string ResolveMoveState()
        {
            if (_isHurtIdle && HasState(MOVE_HURT_STATE)) return MOVE_HURT_STATE;
            if (_isBadIdle  && HasState(MOVE_BAD_STATE))  return MOVE_BAD_STATE;
            return MOVE_STATE;
        }

        private string ResolveTargetingState()
        {
            if (_isHurtIdle && HasState(TARGETING_HURT_STATE)) return TARGETING_HURT_STATE;
            if (_isBadIdle && HasState(TARGETING_BAD_STATE)) return TARGETING_BAD_STATE;
            return TARGETING_STATE;
        }

        private string ResolveJumpState()
        {
            if (_isHurtIdle && HasState(JUMP_HURT_STATE)) return JUMP_HURT_STATE;
            if (_isBadIdle  && HasState(JUMP_BAD_STATE))  return JUMP_BAD_STATE;
            return JUMP_STATE;
        }
        
        private void HandleHealthChanged(Unit changed)
        {
            if (changed != _unit) return;
            EvaluateIdleTier();
        }

        private void HandleRosterChanged(Unit _)
        {
            EvaluateIdleTier();
        }

        private void HandleDialogueStarted(Unit speaker)
        {
            if (speaker != _unit) return;
            _isTalking = true;
            
            if (!IsCurrentlyIdling()) return;

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
        
        private void HandleStatusEffectRemoved(Unit target, StatusEffectInstance effect)
        {
            if (target != _unit) return;
            if (activeHoldInstance == null || effect != activeHoldInstance) return;

            ReleaseHold();
        }

        private void PlayIdleAtTime(string stateName, float normalizedTime)
        {
            if (animator == null) return;
            if (!HasState(stateName)) return;
            if (currentAnimation == DOWNED_STATE) return;

            float frac = normalizedTime % 1f;
            animator.Play(stateName, 0, frac);
            currentAnimation = stateName;
        }
        
        public event System.Action OnCastEffectEvent;
        
        public void AnimEvent_CastEffect() => OnCastEffectEvent?.Invoke();

        public event System.Action OnJumpLaunchEvent;

        public void AnimEvent_JumpLaunch() => OnJumpLaunchEvent?.Invoke();

        public event System.Action OnJumpLandEvent;

        public void AnimEvent_JumpLand() => OnJumpLandEvent?.Invoke();

        public event System.Action<int> OnAbilityEffectEvent;
        
        public void AnimEvent_AbilityEffect0() => OnAbilityEffectEvent?.Invoke(0);
        public void AnimEvent_AbilityEffect1() => OnAbilityEffectEvent?.Invoke(1);
        public void AnimEvent_AbilityEffect2() => OnAbilityEffectEvent?.Invoke(2);
        public void AnimEvent_AbilityEffect3() => OnAbilityEffectEvent?.Invoke(3);

        public void PlayIdle()
        {
            if (isInKnockbackSequence) return;
            string idleState = ResolveIdleState(_isTalking);
            PlayAnimation(idleState, true);
        }

        private void PlayIdleAtRandomTime()
        {
            if (isInKnockbackSequence) return;
            string idleState = ResolveIdleState(_isTalking);
            PlayIdleAtTime(idleState, Random.Range(0f, 1f));
        }
        
        public void PlayMove()
        {
            if (!isInKnockbackSequence)
                PlayAnimation(ResolveMoveState(), true);
        }

        public void PlayTargeting()
        {
            if (isInKnockbackSequence) return;
            PlayAnimation(ResolveTargetingState(), false);
        }

        public void PlayJump()
        {
            if (isInKnockbackSequence) return;

            string jumpState = ResolveJumpState();
            PlayAnimation(jumpState, false);

            StartCoroutine(ReturnToIdleAfterAnimation(jumpState));
        }

        public void PlayAttack()
        {
            if (isInKnockbackSequence) return; 

            PlayAnimation(ATTACK_STATE, false);
            
            StartCoroutine(ReturnToIdleAfterAnimation(ATTACK_STATE));
        }

        public void PlayAnimation(string animationState)
        {
            if (string.IsNullOrEmpty(animationState)) return;
            if (isInKnockbackSequence) return;

            PlayAnimation(animationState, false);
            
            StartCoroutine(ReturnToIdleAfterAnimation(animationState));
        }

        public void PlayAnimationThenHold(string castState, string holdState, string releaseState)
        {
            if (string.IsNullOrEmpty(castState) || isInKnockbackSequence) return;

            if (holdSequence != null)
            {
                StopCoroutine(holdSequence);
                holdSequence = null;
            }

            activeHoldInstance = null;
            activeHoldReleaseState = releaseState;

            PlayAnimation(castState, false);
            holdSequence = StartCoroutine(ReturnToHoldAfterAnimation(castState, holdState));
        }

        public void BindHoldToStatusEffect(StatusEffectInstance instance)
        {
            if (string.IsNullOrEmpty(activeHoldReleaseState)) return;

            activeHoldInstance = instance;
        }

        private IEnumerator ReturnToHoldAfterAnimation(string castState, string holdState)
        {
            yield return new WaitForSeconds(0.1f);

            while (animator.GetCurrentAnimatorStateInfo(0).IsName(castState) &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
            {
                yield return null;
            }

            holdSequence = null;

            if (currentAnimation != castState || isInKnockbackSequence) yield break;

            if (HasState(holdState))
            {
                animator.Play(holdState);
                currentAnimation = holdState;
            }
            else
            {
                Debug.LogWarning($"[UnitAnimator] Hold state '{holdState}' not found on {gameObject.name}, falling back to idle.");
                PlayIdle();
            }
        }
        
        public void ReleaseHold()
        {
            if (holdSequence != null)
            {
                StopCoroutine(holdSequence);
                holdSequence = null;
            }

            string releaseState = activeHoldReleaseState;
            activeHoldInstance = null;
            activeHoldReleaseState = null;

            if (isInKnockbackSequence || currentAnimation == DOWNED_STATE) return;

            if (!string.IsNullOrEmpty(releaseState) && HasState(releaseState))
                PlayAnimation(releaseState); 
            else
                PlayIdle();
        }

        public void PlayHurt()
        {
            if (isInKnockbackSequence) return; 

            PlayAnimation(HURT_STATE, false);
            
            StartCoroutine(ReturnToIdleAfterAnimation(HURT_STATE));
        }
        
        public IEnumerator WaitForHurtAnimation()
        {
            yield return null;

            const float maxWait = 5f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName(HURT_STATE) && stateInfo.normalizedTime >= 1.0f && !stateInfo.loop)
                    yield break;

                if (!stateInfo.IsName(HURT_STATE))
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }
        }
        
        public void PlayDowned()
        {
            if (knockbackSequence != null)
            {
                StopCoroutine(knockbackSequence);
                isInKnockbackSequence = false;
            }
            
            if (animator != null)
                animator.speed = 1f;

            PlayAnimation(DOWNED_STATE, false);
        }

        public void ForcePlayAnimation(string stateName)
        {
            if (animator != null)
            {
                animator.Play(stateName, 0, 0f); 
                currentAnimation = stateName;
            }
        }
        
        public void PlayKnockbackStart()
        {
            if (knockbackSequence != null)
            {
                StopCoroutine(knockbackSequence);
            }

            isInKnockbackSequence = true;
            
            ForcePlayAnimation(KNOCKBACK_START_STATE);
        }

        public void PlayKnockbackEnd()
        {
            SetActiveTurn();
            
            PlayAnimation(KNOCKBACK_END_STATE, false);


            knockbackSequence = StartCoroutine(ReturnToIdleAfterKnockbackEnd());
        }

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

        public void PlaySingleKnockback()
        {
            knockbackSequence = StartCoroutine(SingleKnockbackSequence());
        }
        
        public void SetActiveTurn()
        {
            if (animator != null)
                animator.speed = 1f;
        }
        
        public void SetInactiveTurn()
        {
            if (animator != null)
                animator.speed = inactiveTurnSpeedMultiplier;
        }

        private void PlayAnimation(string stateName, bool canInterrupt)
        {
            if (animator == null) return;
            
            if (!HasState(stateName))
            {
                return;
            }

            if (currentAnimation == DOWNED_STATE && stateName != DOWNED_STATE) return;

            if (isInKnockbackSequence && stateName != DOWNED_STATE &&
                stateName != KNOCKBACK_START_STATE && stateName != KNOCKBACK_END_STATE) return;

            if (!canInterrupt && currentAnimation == stateName) return;

            animator.Play(stateName);

            currentAnimation = stateName;
        }

        public bool HasState(string stateName)
        {
            if (animator == null || string.IsNullOrEmpty(stateName)) return false;

            return animator.HasState(0, Animator.StringToHash(stateName));
        }

        private System.Collections.IEnumerator ReturnToIdleAfterAnimation(string animationState)
        {
            yield return new WaitForSeconds(0.1f);

            while (animator.GetCurrentAnimatorStateInfo(0).IsName(animationState) &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1.0f)
            {
                yield return null;
            }

            if (currentAnimation != DOWNED_STATE && !isInKnockbackSequence)
            {
                PlayIdle();
            }
        }
        
        private IEnumerator ReturnToIdleAfterKnockbackEnd()
        {
            yield return new WaitForSeconds(0.1f);

            while (animator.GetCurrentAnimatorStateInfo(0).IsName(KNOCKBACK_END_STATE) &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 0.95f)
            {
                yield return null;
            }
            
            isInKnockbackSequence = false;
            knockbackSequence = null;
            
            if (currentAnimation != DOWNED_STATE)
            {
                PlayIdle();
            }
        }

        private IEnumerator SingleKnockbackSequence()
        {
            isInKnockbackSequence = true;
            
            PlayAnimation(KNOCKBACK_START_STATE, false);
            
            yield return new WaitForSeconds(0.2f);
            
            PlayAnimation(KNOCKBACK_END_STATE, false);
            
            yield return new WaitForSeconds(0.3f);
            
            isInKnockbackSequence = false;
            knockbackSequence = null;

            if (currentAnimation != DOWNED_STATE)
            {
                PlayIdle();
            }
        }
        
        public bool IsPlayingAnimation(string stateName)
        {
            if (animator == null) return false;
            return animator.GetCurrentAnimatorStateInfo(0).IsName(stateName);
        }

        public float GetCurrentAnimationTime()
        {
            if (animator == null) return 0f;
            return animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
        }

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
        
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void DebugPlayAnimation(string animName)
        {
            switch (animName.ToLower())
            {
                case "idle": PlayIdle(); break;
                case "walk": PlayMove(); break;
                case "attack": PlayAttack(); break;
                case "hurt": PlayHurt(); break;
                case "downed": PlayDowned(); break;
                case "targeting": PlayTargeting(); break;
                case "jump": PlayJump(); break;
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
            yield return new WaitForSeconds(2f); 

            PlayKnockbackEnd();
            yield return new WaitForSeconds(1f);
            
        }

    #if UNITY_EDITOR
        [UnityEngine.ContextMenu("Test Idle")]
        private void TestIdle() => PlayIdle();

        [UnityEngine.ContextMenu("Test Move")]
        private void TestMove() => PlayMove();

        [UnityEngine.ContextMenu("Test Attack")]
        private void TestAttack() => PlayAttack();

        [UnityEngine.ContextMenu("Test Hurt")]
        private void TestHurt() => PlayHurt();

        [UnityEngine.ContextMenu("Test Downed")]
        private void TestDowned() => PlayDowned();

        [UnityEngine.ContextMenu("Test Targeting")]
        private void TestTargeting() => PlayTargeting();

        [UnityEngine.ContextMenu("Test Jump")]
        private void TestJump() => PlayJump();

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
    }
}