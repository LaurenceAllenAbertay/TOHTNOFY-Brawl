using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class Unit : MonoBehaviour
    {
        public CharacterData characterData;

        [Header("Turn Order")]
        [Tooltip("If true, this unit is always placed at the front of the turn order regardless of initiative.")]
        public bool goesFirst = false;

        [Header("Runtime Stats")]
        public int currentHealth;
        public int currentAttack;
        public int currentDefense;
        public Tile currentTile;

        public int currentSpeed;
        public bool canMove;

        // Cache the UnitAnimator for performance
        private UnitAnimator unitAnimator;
        // Cache the sprite renderer
        private SpriteRenderer unitSpriteRenderer;

        // Store the current ability context for timing-based effects
        public AbilityContext currentAbilityContext { get; private set; }
        private List<Unit> currentAbilityTargets;

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = false;

        private enum TargetAnimationType
        {
            Hurt,
            Buff,
            Debuff,
            Knockback,
            Movement
        }

        private void Awake()
        {
            // Cache the UnitAnimator component
            unitAnimator = GetComponent<UnitAnimator>();

            // Cache the sprite renderer
            unitSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

            if (characterData)
            {
                currentHealth = characterData.maxHealth;
                currentAttack = characterData.attack;
                currentDefense = characterData.defense;
                currentSpeed = characterData.speed;
            }

            if (currentTile == null)
            {
                Tile tileBelow = GridManager.Instance?.GetTileAtPosition(transform.position);
                if (tileBelow != null)
                {
                    SetCurrentTile(tileBelow);
                }
            }
            else
            {
                // Ensure the tile knows about us
                SetCurrentTile(currentTile);
            }
        }

        #region Ability Execution

        /// <summary>
        /// Executes ability animation with precise timing for effects and facing
        /// </summary>
        public IEnumerator ExecuteAbilityAnimationSequence(AbilityContext ctx, List<Unit> targets)
        {
            var ability = ctx.ability;

            // Store context for effect execution
            currentAbilityContext = ctx;
            currentAbilityTargets = targets;

            if (enableDebugLogging)
                Debug.Log($"Starting ability animation sequence for {ability.abilityName}");

            // Face the ability direction
            FaceDirection(ctx.aimDir);

            if (!string.IsNullOrEmpty(ability.AnimationState) && unitAnimator != null)
            {
                // Execute effects with proper timing based on animation progress
                yield return StartCoroutine(ExecuteTimedEffects(ctx, targets, 0f));
            }
            else
            {
                // No animation available, use immediate effects
                if (enableDebugLogging)
                    Debug.Log($"No animation found, using immediate effects");

                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
            }

            // Clear context after ability execution
            ClearAbilityContext();
        }
        private IEnumerator ExecuteTimedEffects(AbilityContext ctx, List<Unit> targets, float totalDuration)
        {
            if (enableDebugLogging)
                Debug.Log($"Starting timed effects for {ctx.ability.AnimationState}");

            // STEP 1: Move camera to caster
            var cameraController = FindAnyObjectByType<CameraController>();
            if (cameraController != null)
            {
                Vector3 casterCameraPosition = new Vector3(
                    this.transform.position.x,
                    this.transform.position.y + 2f,
                    this.transform.position.z - 3.5f
                );
                casterCameraPosition = cameraController.ClampToBounds(casterCameraPosition);
                yield return StartCoroutine(TransitionCameraToPosition(cameraController, casterCameraPosition));

                // STEP 1.5: Wait for camera to fully settle
                yield return new WaitForSeconds(1.5f);
            }

            // STEP 2: Get animator
            var animator = unitAnimator?.GetComponent<Animator>();
            if (animator == null)
            {
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
                yield break;
            }

            // STEP 3: Play animation (after camera has arrived and settled)
            if (!string.IsNullOrEmpty(ctx.ability.AnimationState))
                unitAnimator.PlayAnimation(ctx.ability.AnimationState);

            // STEP 4: Subscribe to cast effect Animation Event
            bool castEffectTriggered = false;
            void OnCastEffect()
            {
                if (castEffectTriggered) return;
                castEffectTriggered = true;
                if (enableDebugLogging) Debug.Log("AnimEvent_CastEffect fired");
                SpawnCastEffect(ctx);
            }
            unitAnimator.OnCastEffectEvent += OnCastEffect;

            // STEP 5: Charge vs non-charge diverge here.
            // Charge bypasses the animation-start wait — the charge movement is independent
            // of whether the looping clip started cleanly, so we never fall into ExecuteImmediateEffects.
            if (HasChargeEffect(ctx.ability))
            {
                yield return StartCoroutine(ExecuteChargeSequence(ctx, targets));
                unitAnimator.OnCastEffectEvent -= OnCastEffect;
                if (!castEffectTriggered) SpawnCastEffect(ctx);
                yield break;
            }

            // Non-charge: wait for the animation state to actually start
            float waitTime = 0f;
            while (waitTime < 1f && !animator.GetCurrentAnimatorStateInfo(0).IsName(ctx.ability.AnimationState))
            {
                yield return null;
                waitTime += Time.deltaTime;
            }

            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(ctx.ability.AnimationState))
            {
                Debug.LogWarning($"Animation {ctx.ability.AnimationState} never started, using immediate effects");
                unitAnimator.OnCastEffectEvent -= OnCastEffect;
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
                yield break;
            }

            // STEP 6: Wait for the non-charge animation to finish naturally
            while (true)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (!stateInfo.IsName(ctx.ability.AnimationState)) break;
                if (stateInfo.normalizedTime >= 1.0f && !stateInfo.loop) break;
                yield return null;
            }

            unitAnimator.OnCastEffectEvent -= OnCastEffect;

            // Fallback: cast effect may not have fired if clip has no event yet
            if (!castEffectTriggered)
            {
                if (enableDebugLogging) Debug.Log("AnimEvent_CastEffect fallback triggered");
                SpawnCastEffect(ctx);
            }

            // STEP 6: Animation finished — now handle targets and remaining effects
            yield return StartCoroutine(HandlePostAnimationEffects(ctx, targets));
        }

        /// <summary>
        /// Runs after the caster's non-charge animation ends.
        /// Pans camera to each target, applies hit effects, then returns camera to caster.
        /// </summary>
        private IEnumerator HandlePostAnimationEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (HasSelfKnockbackEffect(ctx.ability))
                StartCoroutine(HandleSelfKnockbackEffects(ctx));

            var nonSelfTargets = targets.Where(t => t != this).ToList();
            if (nonSelfTargets.Count > 0)
                yield return StartCoroutine(HandleCameraTransitionsAndEffects(ctx, nonSelfTargets));

            yield return StartCoroutine(HandleRemainingEffects(ctx, targets));
        }

        /// <summary>
        /// Drives charge ability execution: delegates movement, real-time camera following,
        /// pass-through hit effects, and the stop animation to ChargeEffect, then cleans up.
        /// </summary>
        private IEnumerator ExecuteChargeSequence(AbilityContext ctx, List<Unit> targets)
        {
            var chargeEffect = ctx.ability.effects.OfType<ChargeEffect>().FirstOrDefault();
            if (chargeEffect == null) yield break;

            var cameraController = FindAnyObjectByType<CameraController>();
            yield return StartCoroutine(chargeEffect.ExecuteCharge(ctx, targets, unitAnimator, cameraController));

            // Remaining effects that aren't handled inline by the charge (tile effects, caster self-buffs, etc.)
            yield return StartCoroutine(HandleRemainingEffects(ctx, targets));
        }

        private bool HasSelfKnockbackEffect(Ability ability)
        {
            if (ability?.effects == null) return false;

            foreach (var effect in ability.effects)
            {
                if (effect is KnockbackEffect knockbackEffect && knockbackEffect.applyToSelf)
                {
                    return true;
                }
            }
            return false;
        }

        private IEnumerator HandleSelfKnockbackEffects(AbilityContext ctx)
        {
            var selfKnockbackEffects = ctx.ability.effects.OfType<KnockbackEffect>()
                .Where(kb => kb.applyToSelf).ToList();

            if (selfKnockbackEffects.Count == 0) yield break;

            // Apply self-knockback effects.
            // ApplyKnockbackWithAnimation (inside KnockbackEffect) owns the animation — calling
            // PlayKnockbackStart() here as well was setting isInKnockbackSequence = true prematurely,
            // which blocked the caster's attack animation from playing.
            var selfTargetList = new List<Unit> { this };
            foreach (var effect in selfKnockbackEffects)
            {
                effect.Apply(ctx, selfTargetList);
            }

            // Wait for self-knockback animation + movement to complete
            yield return new WaitForSeconds(1.5f);
        }

        private IEnumerator HandleRemainingEffects(AbilityContext ctx, List<Unit> targets)
        {
            // Handle any effects that weren't processed during camera transitions
            // This includes self-buffs, non-targeting effects, etc.

            var processedEffectTypes = new HashSet<System.Type>
    {
        typeof(KnockbackEffect), // Already handled via HandleSelfKnockbackEffects
        typeof(ChargeEffect)     // Always handled by ExecuteChargeSequence, never via this path
    };

            // Check if we used camera transitions
            bool usedCameraTransitions = ctx.ability.targeting is LineTargeting || ctx.ability.targeting is SingleTargeting;

            if (usedCameraTransitions)
            {
                // These were handled in camera transitions for non-self targets
                processedEffectTypes.Add(typeof(DamageEffect));
                processedEffectTypes.Add(typeof(MovementEffect));
                processedEffectTypes.Add(typeof(StatusEffect));
            }

            // Apply remaining effects
            foreach (var effect in ctx.ability.effects)
            {
                if (processedEffectTypes.Contains(effect.GetType())) continue;

                // Special handling for self-targeting effects
                if (effect is StatusEffect statusEffect)
                {
                    var selfApplications = statusEffect.statusesToApply.Where(sa =>
                        sa.applyTo == StatusEffect.ApplicationTarget.Caster ||
                        sa.applyTo == StatusEffect.ApplicationTarget.Both).ToList();

                    if (selfApplications.Count > 0)
                    {
                        // Apply self-buffs/debuffs
                        effect.Apply(ctx, new List<Unit> { this });

                        // Play appropriate animation
                        bool isBuffEffect = selfApplications.Any(sa => IsBuffEffectType(sa.statusEffectData.effectType));
                        if (isBuffEffect)
                        {
                            PlayTargetAnimation(this, TargetAnimationType.Buff);
                        }
                        else
                        {
                            PlayTargetAnimation(this, TargetAnimationType.Debuff);
                        }

                        yield return new WaitForSeconds(0.6f);
                    }
                }
                else
                {
                    // Apply other effects normally
                    effect.Apply(ctx, targets);
                }
            }
        }

        private bool IsBuffEffectType(StatusEffectType effectType)
        {
            switch (effectType)
            {
                case StatusEffectType.Shielded:
                case StatusEffectType.Guarded:
                case StatusEffectType.Untargetable:
                case StatusEffectType.AttackUp:
                case StatusEffectType.DefenseUp:
                case StatusEffectType.SpeedUp:
                case StatusEffectType.Hastened:
                case StatusEffectType.Urged:
                case StatusEffectType.Healthy:
                case StatusEffectType.Saturated:
                case StatusEffectType.Alerted:
                    return true;
                default:
                    return false;
            }
        }

        private bool HasChargeEffect(Ability ability)
        {
            if (ability?.effects == null) return false;
            return ability.effects.Any(effect => effect is ChargeEffect);
        }

        private IEnumerator HandleCameraTransitionsAndEffects(AbilityContext ctx, List<Unit> targets)
        {
            var cameraController = FindAnyObjectByType<CameraController>();
            if (cameraController == null)
            {
                ApplyAbilityEffectsToTargets(ctx, targets);
                SpawnHitEffects(ctx, targets);
                yield break;
            }

            bool shouldUseTransitions = ctx.ability.targeting is LineTargeting || ctx.ability.targeting is SingleTargeting;
            bool hasMovementEffect = ctx.ability.effects.Any(e => e is MovementEffect && !((MovementEffect)e).applyToCaster);

            if (!shouldUseTransitions && !hasMovementEffect)
            {
                ApplyAbilityEffectsToTargets(ctx, targets);
                SpawnHitEffects(ctx, targets);
                yield break;
            }

            yield return StartCoroutine(HandleSingleTargetingEffects(ctx, targets, cameraController));
        }

        private void ApplyAbilityEffectsToTargets(AbilityContext ctx, List<Unit> targets)
        {
            // Apply effects only to the specified targets (filtering out already processed effects)
            foreach (var effect in ctx.ability.effects)
            {
                // Skip self-knockback (already handled)
                if (effect is KnockbackEffect knockbackEffect && knockbackEffect.applyToSelf)
                    continue;

                // Skip self-only status effects (will be handled in HandleRemainingEffects)
                if (effect is StatusEffect statusEffect)
                {
                    bool isSelfOnly = statusEffect.statusesToApply.All(sa =>
                        sa.applyTo == StatusEffect.ApplicationTarget.Caster);
                    if (isSelfOnly) continue;
                }

                effect.Apply(ctx, targets);
            }
        }

        private IEnumerator HandleSingleTargetingEffects(AbilityContext ctx, List<Unit> targets, CameraController cameraController)
        {
            foreach (var target in targets)
            {
                if (target == null) continue;

                yield return StartCoroutine(TransitionCameraToUnit(cameraController, target));

                var singleTargetList = new List<Unit> { target };
                yield return StartCoroutine(PlayTargetEffectsWithAnimation(ctx, singleTargetList));

                yield return new WaitForSeconds(0.3f);
            }

            yield return StartCoroutine(TransitionCameraToPosition(cameraController, cameraController.ClampToBounds(new Vector3(
                this.transform.position.x,
                this.transform.position.y + 2f,
                this.transform.position.z - 3.5f))));
        }

        private IEnumerator TransitionCameraToUnit(CameraController cameraController, Unit targetUnit)
        {
            Vector3 targetPosition = new Vector3(
                targetUnit.transform.position.x,
                targetUnit.transform.position.y + 2f,
                targetUnit.transform.position.z - 3.5f
            );

            targetPosition = cameraController.ClampToBounds(targetPosition);

            yield return StartCoroutine(TransitionCameraToPosition(cameraController, targetPosition));
        }

        private IEnumerator TransitionCameraToPosition(CameraController cameraController, Vector3 targetPosition)
        {
            Vector3 startPosition = cameraController.transform.position;
            float transitionDuration = 1f; // Always 1 second as specified
            float elapsed = 0f;

            while (elapsed < transitionDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / transitionDuration;

                // Smooth transition
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                cameraController.transform.position = Vector3.Lerp(startPosition, targetPosition, smoothT);

                yield return null;
            }

            // Ensure exact final position
            cameraController.transform.position = targetPosition;
        }

        private IEnumerator PlayTargetEffectsWithAnimation(AbilityContext ctx, List<Unit> targets)
        {
            // Group effects by type and execution order
            // Exclude applyToSelf knockbacks — those are fired concurrently in Step 3.5 of ExecuteTimedEffects
            var knockbackEffects = ctx.ability.effects.OfType<KnockbackEffect>()
                .Where(kb => !kb.applyToSelf)
                .ToList();
            var damageEffects = ctx.ability.effects.OfType<DamageEffect>().ToList();
            var movementEffects = ctx.ability.effects.OfType<MovementEffect>().Where(me => !me.applyToCaster).ToList();
            var statusEffects = ctx.ability.effects.OfType<StatusEffect>().ToList();
            var otherEffects = ctx.ability.effects.Where(e => !(e is KnockbackEffect || e is DamageEffect || e is MovementEffect || e is StatusEffect)).ToList();

            // Step 1: Handle Knockback Effects (if present, skip hurt animation)
            bool hasKnockback = knockbackEffects.Count > 0;
            if (hasKnockback)
            {
                foreach (var target in targets)
                {
                    PlayTargetAnimation(target, TargetAnimationType.Knockback);
                }

                // Apply knockback effects
                foreach (var effect in knockbackEffects)
                {
                    effect.Apply(ctx, targets);
                }

                // Wait for knockback animations to complete
                yield return new WaitForSeconds(1f);
            }

            // Step 2: Handle Damage Effects (play hurt animation if no knockback)
            if (!hasKnockback && damageEffects.Count > 0)
            {
                foreach (var target in targets)
                {
                    PlayTargetAnimation(target, TargetAnimationType.Hurt);
                }

                // Spawn hit effects and apply damage
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects)
                {
                    effect.Apply(ctx, targets);
                }

                yield return new WaitForSeconds(0.8f);
            }
            else if (damageEffects.Count > 0)
            {
                // Apply damage without hurt animation (knockback already played)
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects)
                {
                    effect.Apply(ctx, targets);
                }
            }

            // Step 3: Handle Movement Effects
            if (movementEffects.Count > 0)
            {
                var cameraController = FindAnyObjectByType<CameraController>();

                // Fire movement via Apply — this was already working and is left unchanged
                foreach (var effect in movementEffects)
                    effect.Apply(ctx, targets);

                // Camera follow: track the first moving target's live position for the movement duration.
                // The camera is already panned to the target by HandleSingleTargetingEffects, so the
                // offset captured here is the correct one to maintain throughout the movement.
                if (cameraController != null && targets.Count > 0)
                {
                    var trackedTarget = targets[0];
                    Vector3 cameraOffset = cameraController.transform.position - trackedTarget.transform.position;
                    float trackDuration = movementEffects.Max(e => e.movementDurationPerTile * e.moveDistance) + 0.3f;
                    float elapsed = 0f;

                    while (elapsed < trackDuration)
                    {
                        cameraController.transform.position = cameraController.ClampToBounds(
                            trackedTarget.transform.position + cameraOffset);
                        elapsed += Time.deltaTime;
                        yield return null;
                    }
                }
                else
                {
                    yield return new WaitForSeconds(1f);
                }
            }

            // Step 4: Handle Buff Effects
            var buffStatusEffects = statusEffects.Where(se => HasBuffEffects(se)).ToList();
            if (buffStatusEffects.Count > 0)
            {
                foreach (var target in targets)
                {
                    PlayTargetAnimation(target, TargetAnimationType.Buff);
                }

                foreach (var effect in buffStatusEffects)
                {
                    effect.Apply(ctx, targets);
                }

                yield return new WaitForSeconds(0.6f);
            }

            // Step 5: Handle Debuff Effects
            var debuffStatusEffects = statusEffects.Where(se => !HasBuffEffects(se)).ToList();
            if (debuffStatusEffects.Count > 0)
            {
                foreach (var target in targets)
                {
                    PlayTargetAnimation(target, TargetAnimationType.Debuff);
                }

                foreach (var effect in debuffStatusEffects)
                {
                    effect.Apply(ctx, targets);
                }

                yield return new WaitForSeconds(0.6f);
            }

            // Step 6: Handle Other Effects
            if (otherEffects.Count > 0)
            {
                foreach (var effect in otherEffects)
                {
                    effect.Apply(ctx, targets);
                }

                yield return new WaitForSeconds(0.5f);
            }
        }

        private void PlayTargetAnimation(Unit target, TargetAnimationType animationType)
        {
            if (target == null) return;

            var targetAnimator = target.GetComponent<UnitAnimator>();
            if (targetAnimator == null) return;

            switch (animationType)
            {
                case TargetAnimationType.Hurt:
                    targetAnimator.PlayHurt();
                    break;
                case TargetAnimationType.Buff:
                    // Try to play buff animation, fallback to custom animation or hurt
                    if (targetAnimator.HasState("Buff"))
                        targetAnimator.PlayAnimation("Buff");
                    else
                        targetAnimator.PlayHurt(); // Fallback
                    break;
                case TargetAnimationType.Debuff:
                    // Try to play debuff animation, fallback to hurt
                    if (targetAnimator.HasState("Debuff"))
                        targetAnimator.PlayAnimation("Debuff");
                    else
                        targetAnimator.PlayHurt(); // Fallback
                    break;
                case TargetAnimationType.Knockback:
                    targetAnimator.PlayKnockbackStart();
                    break;
                case TargetAnimationType.Movement:
                    targetAnimator.PlayMove();
                    break;
            }
        }

        private bool HasBuffEffects(StatusEffect statusEffect)
        {
            foreach (var statusApp in statusEffect.statusesToApply)
            {
                switch (statusApp.statusEffectData.effectType)
                {
                    case StatusEffectType.Shielded:
                    case StatusEffectType.Guarded:
                    case StatusEffectType.Untargetable:
                    case StatusEffectType.AttackUp:
                    case StatusEffectType.DefenseUp:
                    case StatusEffectType.SpeedUp:
                    case StatusEffectType.Hastened:
                    case StatusEffectType.Urged:
                    case StatusEffectType.Healthy:
                    case StatusEffectType.Saturated:
                    case StatusEffectType.Alerted:
                        return true;
                }
            }
            return false;
        }

        // NEW METHOD: Proper coroutine for getting animation duration
        private IEnumerator GetAnimationDurationCoroutine(string animationStateName, System.Action<float> callback)
        {
            float duration = 0f;

            if (unitAnimator == null || string.IsNullOrEmpty(animationStateName))
            {
                callback(duration);
                yield break;
            }

            // Get the animator component
            Animator animator = unitAnimator.GetComponent<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                callback(duration);
                yield break;
            }

            // Method 1: Search through all animation clips
            AnimationClip[] clips = animator.runtimeAnimatorController.animationClips;
            foreach (var clip in clips)
            {
                if (clip.name == animationStateName || clip.name.Contains(animationStateName))
                {
                    duration = clip.length;
                    callback(duration);
                    yield break;
                }
            }

            // Method 2: If exact match not found, try to get from animator state
            if (animator.HasState(0, Animator.StringToHash(animationStateName)))
            {
                // Store current state
                var currentState = animator.GetCurrentAnimatorStateInfo(0);
                float currentTime = currentState.normalizedTime;

                // Play the animation to get its info
                animator.Play(animationStateName, 0, 0f);
                yield return null; // Wait one frame for animator to update

                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.IsName(animationStateName))
                {
                    duration = stateInfo.length;
                }

                // Restore previous state if possible
                if (currentState.shortNameHash != 0)
                {
                    animator.Play(currentState.shortNameHash, 0, currentTime);
                }
            }

            callback(duration);
        }

        private IEnumerator ExecuteImmediateEffects(AbilityContext ctx, List<Unit> targets)
        {
            // For abilities without animations, trigger effects with minimal delays
            SpawnCastEffect(ctx);

            yield return new WaitForSeconds(0.1f);
            ApplyAbilityEffects(ctx, targets);

            yield return new WaitForSeconds(0.05f);
            SpawnHitEffects(ctx, targets);

            yield return new WaitForSeconds(0.1f);
        }

        private void SpawnCastEffect(AbilityContext ctx)
        {
            var ability = ctx.ability;
            if (ability.CastEffectPrefab == null) return;

            Vector3 adjustedOffset = ability.CastEffectOffset;
            Quaternion rotation = transform.rotation;

            // Handle directional rotation
            if (ctx.aimDir == Vector2Int.left)
            {
                adjustedOffset.x = -adjustedOffset.x;
                rotation = Quaternion.Euler(0, 180, 0);
            }
            else if (ctx.aimDir == Vector2Int.up)
            {
                rotation = Quaternion.Euler(0, 90, 0);
            }
            else if (ctx.aimDir == Vector2Int.down)
            {
                rotation = Quaternion.Euler(0, 270, 0);
            }

            Vector3 spawnPos = transform.position + adjustedOffset;
            var effect = Instantiate(ability.CastEffectPrefab, spawnPos, rotation);

            if (ability.ParentCastEffectToCaster)
            {
                effect.transform.SetParent(transform);
                effect.transform.localPosition = adjustedOffset;
            }

            Destroy(effect, 3f);
        }

        private void SpawnHitEffects(AbilityContext ctx, List<Unit> targets)
        {
            var ability = ctx.ability;
            if (ability.HitEffectPrefab == null || targets == null) return;

            Debug.Log($"Spawning hit effects for {ability.abilityName}");

            foreach (var target in targets)
            {
                if (target != null)
                {
                    Vector3 spawnPos = target.transform.position + ability.HitEffectOffset;
                    var effect = Instantiate(ability.HitEffectPrefab, spawnPos, Quaternion.identity);

                    if (ability.ParentHitEffectToTarget)
                    {
                        effect.transform.SetParent(target.transform);
                        effect.transform.localPosition = ability.HitEffectOffset;
                    }

                    Destroy(effect, 2f);
                }
            }
        }

        private void ApplyAbilityEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (ctx?.ability == null || targets == null) return;

            foreach (var effect in ctx.ability.effects)
            {
                if (effect != null)
                {
                    effect.Apply(ctx, targets);
                }
            }
        }

        private void ClearAbilityContext()
        {
            currentAbilityContext = null;
            currentAbilityTargets = null;

            // Only return to natural facing if NOT in a knockback sequence
            if (unitAnimator != null && !unitAnimator.IsInKnockbackSequence)
            {
                StartCoroutine(ReturnToNaturalFacingDelayed(0.1f));
            }
        }

        #endregion

        #region Movement and Facing

        public void FaceDirection(Vector2Int direction)
        {
            if (unitSpriteRenderer == null) return;

            // Flip sprite based on horizontal direction
            if (direction == Vector2Int.left)
            {
                unitSpriteRenderer.flipX = true;
            }
            else if (direction == Vector2Int.right)
            {
                unitSpriteRenderer.flipX = false;
            }
        }

        /// <summary>
        /// Returns the unit to its natural facing direction based on map configuration
        /// </summary>
        public void ReturnToNaturalFacing()
        {
            Vector2Int naturalFacing = MapManager.GetNaturalFacing(this);
            FaceDirection(naturalFacing);
        }

        // Sets the unit's current tile, updating the tile's reference as well (INSTANT movement)
        public void SetCurrentTile(Tile newTile)
        {
            // Clear old tile's reference if needed
            if (currentTile != null && currentTile.currentUnit == this)
            {
                currentTile.currentUnit = null;
            }

            // Assign new tile and link both ways
            currentTile = newTile;
            if (currentTile != null)
            {
                currentTile.currentUnit = this;
                transform.position = currentTile.transform.position;
            }

            // Notify other systems
            UnitManager.NotifyUnitMoved(this);
        }

        // Sets the tile reference without moving the transform (for animated movement)
        public void SetCurrentTileLogical(Tile newTile)
        {
            // Clear old tile's reference if needed
            if (currentTile != null && currentTile.currentUnit == this)
            {
                currentTile.currentUnit = null;
            }

            // Assign new tile and link both ways, but DON'T move the transform
            currentTile = newTile;
            if (currentTile != null)
            {
                currentTile.currentUnit = this;
            }

            // Notify other systems
            UnitManager.NotifyUnitMoved(this);
        }

        /// <summary>
        /// Smoothly animates unit to target tile with knockback-style movement and returns to natural facing after completion
        /// </summary>
        public void AnimateToTile(Tile targetTile, float duration, System.Action onComplete = null)
        {
            if (targetTile == null) return;
            StartCoroutine(SmoothKnockbackMovement(targetTile, duration, onComplete));
        }

        /// <summary>
        /// Handles smooth movement animation with automatic return to natural facing
        /// </summary>
        private IEnumerator SmoothKnockbackMovement(Tile targetTile, float duration, System.Action onComplete)
        {
            Vector3 startPos = transform.position;
            Vector3 endPos = targetTile.transform.position;

            // Update logical tile reference immediately
            SetCurrentTileLogical(targetTile);

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;

                // Knockback-style easing: fast start, slow end
                float easedT = 1f - Mathf.Pow(1f - t, 3f);

                transform.position = Vector3.Lerp(startPos, endPos, easedT);
                yield return null;
            }

            // Ensure exact final position
            transform.position = endPos;

            // REMOVED: Don't return to natural facing here - let the knockback animation sequence handle it
            // yield return StartCoroutine(ReturnToNaturalFacingDelayed(0.1f));

            onComplete?.Invoke();
        }

        #endregion

        #region Turn Management

        /// <summary>
        /// Initializes turn state and ensures proper facing direction
        /// </summary>
        public virtual void StartTurn()
        {
            canMove = true;

            // Ensure correct facing at turn start
            ReturnToNaturalFacing();

            // Set animation speed for active turn
            if (unitAnimator != null)
            {
                unitAnimator.SetActiveTurn();
            }
        }

        /// <summary>
        /// Finalizes turn state and ensures proper facing direction
        /// </summary>
        public virtual void EndTurn()
        {
            // Return to natural facing at turn end
            StartCoroutine(ReturnToNaturalFacingDelayed(0.1f));

            // Set animation speed for inactive turn
            if (unitAnimator != null)
            {
                unitAnimator.SetInactiveTurn();
            }
        }

        /// <summary>
        /// Returns to natural facing after a short delay to ensure animations complete
        /// </summary>
        private IEnumerator ReturnToNaturalFacingDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToNaturalFacing();
        }

        #endregion

        #region Lifecycle

        void Start()
        {
            if (!UnitManager.AllUnits.Contains(this))
            {
                UnitManager.RegisterUnit(this);
            }
        }

        void OnDestroy()
        {
            UnitManager.UnregisterUnit(this);
        }

        #endregion

        #region Combat and Status Effects

        public virtual void ReceiveDamage(int amount)
        {
            // Check for damage immunity effects
            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Shielded))
                {
                    Debug.Log($"{name} is shielded - no damage taken!");
                    return;
                }

                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Guarded))
                {
                    var guardEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Guarded);
                    guardEffect.effectPower -= amount;

                    if (guardEffect.effectPower <= 0)
                    {
                        StatusEffectManager.Instance.RemoveStatusEffect(this, guardEffect);
                        Debug.Log($"{name}'s guard was broken!");
                    }
                    else
                    {
                        Debug.Log($"{name}'s guard absorbed {amount} damage!");
                    }
                    return;
                }

                // Check for Alerted (dodge chance)
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Alerted))
                {
                    var alertEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Alerted);
                    if (Random.Range(0f, 1f) < alertEffect.effectPower)
                    {
                        Debug.Log($"{name} dodged the attack!");
                        StatusEffectManager.Instance.RemoveStatusEffect(this, alertEffect);
                        return;
                    }
                }
            }

            // Apply normal damage
            currentHealth -= amount;
            Debug.Log($"{name} took {amount} damage. HP now {currentHealth}");

            if (currentHealth <= 0)
            {
                Debug.Log($"{name} was defeated.");
                UnitManager.NotifyUnitDied(this);
            }
        }

        public virtual bool CanMove()
        {
            if (!canMove) return false;

            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Ensnared))
                    return false;
            }

            return true;
        }

        public virtual int GetEffectiveMovementRange()
        {
            int baseSpeed = currentSpeed;

            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(this, StatusEffectType.Encumbered))
                {
                    var encumberedEffect = StatusEffectManager.Instance.GetStatusEffect(this, StatusEffectType.Encumbered);
                    baseSpeed = Mathf.Max(1, baseSpeed - Mathf.RoundToInt(encumberedEffect.effectPower));
                }
            }

            return baseSpeed;
        }

        public virtual bool CanTarget(Unit unit)
        {
            if (unit == null) return false;
            if (unit == this) return false;

            // Check if target is untargetable
            if (StatusEffectManager.Instance != null)
            {
                if (StatusEffectManager.Instance.HasStatusEffect(unit, StatusEffectType.Untargetable))
                {
                    return false;
                }
            }

            // Base targeting rule: can target enemies but not allies
            return !IsAllyOf(unit);
        }

        protected bool IsAllyOf(Unit other)
        {
            if (other == null) return false;

            // Simple alliance check: Players vs Enemies
            bool thisIsEnemy = this is EnemyUnit;
            bool otherIsEnemy = other is EnemyUnit;
            return thisIsEnemy == otherIsEnemy;
        }

        #endregion


        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        public void EnableDebugLogging(bool enable)
        {
            enableDebugLogging = enable;
        }
    }
}