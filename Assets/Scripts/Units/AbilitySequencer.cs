using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Owns the complete ability animation pipeline for one unit.
    /// Add this component to the same prefab as Unit.
    ///
    /// Ability.cs starts the sequence via Unit.ExecuteAbilityAnimationSequence,
    /// which calls BeginSequence() here. CombatManager and UnitAI poll
    /// CurrentAbilityContext != null to know whether the sequence is still running.
    /// </summary>
    public class AbilitySequencer : MonoBehaviour
    {
        #region Public State

        public AbilityContext CurrentAbilityContext { get; private set; }

        #endregion

        #region Private Fields

        private Unit unit;
        private UnitAnimator unitAnimator;
        private CameraController cameraController;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = false;

        private enum TargetAnimationType { Hurt, Buff, Debuff, Knockback, Movement }

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            unit = GetComponent<Unit>();
            unitAnimator = GetComponent<UnitAnimator>();
        }

        void Start()
        {
            // FindAnyObjectByType is acceptable here because there is only ever one camera controller
            cameraController = FindAnyObjectByType<CameraController>();
        }

        #endregion

        #region Public Entry Point

        /// <summary>
        /// Starts the ability animation pipeline on this MonoBehaviour.
        /// Unit.ExecuteAbilityAnimationSequence polls CurrentAbilityContext to know when done.
        /// </summary>
        public void BeginSequence(AbilityContext ctx, List<Unit> targets)
        {
            StartCoroutine(RunSequence(ctx, targets));
        }

        #endregion

        #region Core Sequence

        private IEnumerator RunSequence(AbilityContext ctx, List<Unit> targets)
        {
            CurrentAbilityContext = ctx;
            unit.FaceDirection(ctx.aimDir);

            if (!string.IsNullOrEmpty(ctx.ability.AnimationState) && unitAnimator != null)
                yield return StartCoroutine(ExecuteTimedEffects(ctx, targets));
            else
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));

            ClearContext();
        }

        private IEnumerator ExecuteTimedEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (enableDebugLogging)
                Debug.Log($"[AbilitySequencer:{unit.name}] Starting timed effects for {ctx.ability.AnimationState}");

            // STEP 1: Pan camera to caster — animation begins as soon as the transition completes
            if (cameraController != null)
            {
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));
            }

            // STEP 2: Get animator
            var animator = unitAnimator?.GetComponent<Animator>();
            if (animator == null)
            {
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
                yield break;
            }

            // STEP 3: Play animation
            if (!string.IsNullOrEmpty(ctx.ability.AnimationState))
                unitAnimator.PlayAnimation(ctx.ability.AnimationState);

            // STEP 4: Subscribe to cast-effect animation event
            bool castEffectTriggered = false;
            void OnCastEffect()
            {
                if (castEffectTriggered) return;
                castEffectTriggered = true;
                if (enableDebugLogging) Debug.Log("[AbilitySequencer] AnimEvent_CastEffect fired");
                SpawnCastEffect(ctx);
            }
            unitAnimator.OnCastEffectEvent += OnCastEffect;

            // STEP 5: Charge abilities bypass the wait-for-animation-start logic
            if (HasChargeEffect(ctx.ability))
            {
                yield return StartCoroutine(ExecuteChargeSequence(ctx, targets));
                unitAnimator.OnCastEffectEvent -= OnCastEffect;
                if (!castEffectTriggered) SpawnCastEffect(ctx);
                yield break;
            }

            // Non-charge: wait for the animation to actually start
            float waitTime = 0f;
            while (waitTime < 1f && !animator.GetCurrentAnimatorStateInfo(0).IsName(ctx.ability.AnimationState))
            {
                yield return null;
                waitTime += Time.deltaTime;
            }

            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(ctx.ability.AnimationState))
            {
                Debug.LogWarning($"[AbilitySequencer] Animation {ctx.ability.AnimationState} never started, using immediate effects");
                unitAnimator.OnCastEffectEvent -= OnCastEffect;
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
                yield break;
            }

            // STEP 6: Wait for the animation to finish
            while (true)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (!stateInfo.IsName(ctx.ability.AnimationState)) break;
                if (stateInfo.normalizedTime >= 1.0f && !stateInfo.loop) break;
                yield return null;
            }

            unitAnimator.OnCastEffectEvent -= OnCastEffect;

            if (!castEffectTriggered)
            {
                if (enableDebugLogging) Debug.Log("[AbilitySequencer] AnimEvent_CastEffect fallback triggered");
                SpawnCastEffect(ctx);
            }

            // STEP 7: Post-animation — camera pan + effects
            yield return StartCoroutine(HandlePostAnimationEffects(ctx, targets));
        }

        private IEnumerator HandlePostAnimationEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (HasSelfKnockbackEffect(ctx.ability))
                StartCoroutine(HandleSelfKnockbackEffects(ctx));

            var nonSelfTargets = targets.Where(t => t != unit).ToList();
            if (nonSelfTargets.Count > 0)
                yield return StartCoroutine(HandleCameraTransitionsAndEffects(ctx, nonSelfTargets));
            else if (ctx.ability.canExecuteWithoutTargets)
            {
                // No units hit but the ability can still fire — spawn hit effects on
                // empty traversal tiles so the cast still has visual feedback.
                SpawnHitEffectsOnTraversalTiles(ctx);
            }

            yield return StartCoroutine(HandleRemainingEffects(ctx, targets));
        }

        private IEnumerator ExecuteChargeSequence(AbilityContext ctx, List<Unit> targets)
        {
            var chargeEffect = ctx.ability.effects.OfType<ChargeEffect>().FirstOrDefault();
            if (chargeEffect == null) yield break;

            yield return StartCoroutine(chargeEffect.ExecuteCharge(ctx, targets, unitAnimator, cameraController));
            yield return StartCoroutine(HandleRemainingEffects(ctx, targets));
        }

        #endregion

        #region Camera + Effect Dispatch

        private IEnumerator HandleCameraTransitionsAndEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (cameraController == null)
            {
                ApplyAbilityEffectsToTargets(ctx, targets);
                SpawnHitEffects(ctx, targets);
                yield break;
            }

            bool shouldUseTransitions = ctx.ability.targeting is LineTargeting || ctx.ability.targeting is SingleTargeting;
            bool hasMovementEffect = ctx.ability.effects.Any(e => e is MovementEffect me && !me.applyToCaster);

            if (!shouldUseTransitions && !hasMovementEffect)
            {
                ApplyAbilityEffectsToTargets(ctx, targets);
                SpawnHitEffects(ctx, targets);
                SpawnHitEffectsOnTraversalTiles(ctx);
                yield break;
            }

            yield return StartCoroutine(HandleSingleTargetingEffects(ctx, targets));
        }

        private IEnumerator HandleSingleTargetingEffects(AbilityContext ctx, List<Unit> targets)
        {
            foreach (var target in targets)
            {
                if (target == null) continue;

                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(target)));

                yield return StartCoroutine(PlayTargetEffectsWithAnimation(ctx, new List<Unit> { target }));
                yield return new WaitForSeconds(0.3f);
            }

            // Return camera to caster
            yield return StartCoroutine(cameraController.TransitionTo(
                cameraController.UnitFocusPosition(unit)));
        }

        private IEnumerator PlayTargetEffectsWithAnimation(AbilityContext ctx, List<Unit> targets)
        {
            var knockbackEffects = ctx.ability.effects.OfType<KnockbackEffect>()
                .Where(kb => !kb.applyToSelf).ToList();
            var damageEffects = ctx.ability.effects.OfType<DamageEffect>().ToList();
            var movementEffects = ctx.ability.effects.OfType<MovementEffect>()
                .Where(me => !me.applyToCaster).ToList();
            var statusEffects = ctx.ability.effects.OfType<StatusEffect>().ToList();
            var otherEffects = ctx.ability.effects.Where(e =>
                !(e is KnockbackEffect || e is DamageEffect || e is MovementEffect || e is StatusEffect)).ToList();

            // Knockback
            bool hasKnockback = knockbackEffects.Count > 0;
            if (hasKnockback)
            {
                foreach (var target in targets) PlayTargetAnimation(target, TargetAnimationType.Knockback);
                foreach (var effect in knockbackEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(1f);
            }

            // Damage
            if (!hasKnockback && damageEffects.Count > 0)
            {
                foreach (var target in targets) PlayTargetAnimation(target, TargetAnimationType.Hurt);
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.8f);
            }
            else if (damageEffects.Count > 0)
            {
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects) effect.Apply(ctx, targets);
            }

            // Movement
            if (movementEffects.Count > 0)
            {
                foreach (var effect in movementEffects) effect.Apply(ctx, targets);

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

            // Buffs
            var buffStatusEffects = statusEffects.Where(se => HasBuffEffects(se)).ToList();
            if (buffStatusEffects.Count > 0)
            {
                foreach (var target in targets) PlayTargetAnimation(target, TargetAnimationType.Buff);
                foreach (var effect in buffStatusEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.6f);
            }

            // Debuffs
            var debuffStatusEffects = statusEffects.Where(se => !HasBuffEffects(se)).ToList();
            if (debuffStatusEffects.Count > 0)
            {
                foreach (var target in targets) PlayTargetAnimation(target, TargetAnimationType.Debuff);
                foreach (var effect in debuffStatusEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.6f);
            }

            // Other
            if (otherEffects.Count > 0)
            {
                foreach (var effect in otherEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.5f);
            }
        }

        private IEnumerator HandleRemainingEffects(AbilityContext ctx, List<Unit> targets)
        {
            var processedTypes = new HashSet<System.Type>
            {
                typeof(KnockbackEffect),
                typeof(ChargeEffect)
            };

            bool usedCameraTransitions = ctx.ability.targeting is LineTargeting
                                      || ctx.ability.targeting is SingleTargeting;
            if (usedCameraTransitions)
            {
                processedTypes.Add(typeof(DamageEffect));
                processedTypes.Add(typeof(MovementEffect));
                processedTypes.Add(typeof(StatusEffect));
            }

            foreach (var effect in ctx.ability.effects)
            {
                if (processedTypes.Contains(effect.GetType())) continue;

                if (effect is StatusEffect statusEffect)
                {
                    var selfApplications = statusEffect.statusesToApply.Where(sa =>
                        sa.applyTo == StatusEffect.ApplicationTarget.Caster ||
                        sa.applyTo == StatusEffect.ApplicationTarget.Both).ToList();

                    if (selfApplications.Count > 0)
                    {
                        effect.Apply(ctx, new List<Unit> { unit });
                        bool isBuffEffect = selfApplications.Any(sa =>
                            IsBuffEffectType(sa.statusEffectData.effectType));
                        PlayTargetAnimation(unit,
                            isBuffEffect ? TargetAnimationType.Buff : TargetAnimationType.Debuff);
                        yield return new WaitForSeconds(0.6f);
                    }
                }
                else
                {
                    yield return StartCoroutine(ApplyEffectWithOptionalTeleportCamera(ctx, targets, effect));
                }
            }
        }

        private IEnumerator HandleSelfKnockbackEffects(AbilityContext ctx)
        {
            var selfKnockbackEffects = ctx.ability.effects.OfType<KnockbackEffect>()
                .Where(kb => kb.applyToSelf).ToList();
            if (selfKnockbackEffects.Count == 0) yield break;

            var selfTargetList = new List<Unit> { unit };
            foreach (var effect in selfKnockbackEffects)
                effect.Apply(ctx, selfTargetList);

            yield return new WaitForSeconds(1.5f);
        }

        private IEnumerator ExecuteImmediateEffects(AbilityContext ctx, List<Unit> targets)
        {
            SpawnCastEffect(ctx);
            yield return new WaitForSeconds(0.1f);
            yield return StartCoroutine(ApplyAbilityEffectsWithCamera(ctx, targets));
            yield return new WaitForSeconds(0.05f);
            SpawnHitEffects(ctx, targets);
            yield return new WaitForSeconds(0.1f);
        }

        #endregion

        #region Effect Application Helpers

        private void ApplyAbilityEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (ctx?.ability == null || targets == null) return;
            foreach (var effect in ctx.ability.effects)
                effect?.Apply(ctx, targets);
        }

        private IEnumerator ApplyAbilityEffectsWithCamera(AbilityContext ctx, List<Unit> targets)
        {
            if (ctx?.ability == null || targets == null) yield break;

            foreach (var effect in ctx.ability.effects)
                yield return StartCoroutine(ApplyEffectWithOptionalTeleportCamera(ctx, targets, effect));
        }

        private void ApplyAbilityEffectsToTargets(AbilityContext ctx, List<Unit> targets)
        {
            foreach (var effect in ctx.ability.effects)
            {
                if (effect is KnockbackEffect knockbackEffect && knockbackEffect.applyToSelf)
                    continue;

                if (effect is StatusEffect statusEffect)
                {
                    bool isSelfOnly = statusEffect.statusesToApply.All(sa =>
                        sa.applyTo == StatusEffect.ApplicationTarget.Caster);
                    if (isSelfOnly) continue;
                }

                effect.Apply(ctx, targets);
            }
        }

        private IEnumerator ApplyEffectWithOptionalTeleportCamera(
            AbilityContext ctx,
            List<Unit> targets,
            AbilityEffect effect)
        {
            if (effect == null) yield break;

            if (cameraController != null &&
                effect is TeleportEffect teleportEffect &&
                teleportEffect.TryGetValidDestination(ctx, out var destinationTile))
            {
                Vector3 focusPosition = cameraController.WorldFocusPosition(destinationTile.transform.position);
                yield return StartCoroutine(cameraController.TransitionTo(focusPosition, 0.5f));
            }

            effect.Apply(ctx, targets);
        }

        #endregion

        #region VFX Delegation

        private void SpawnCastEffect(AbilityContext ctx)
        {
            if (CombatVFXManager.Instance != null)
                CombatVFXManager.Instance.SpawnCastEffect(ctx, unit.transform);
        }

        private void SpawnHitEffects(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (CombatVFXManager.Instance != null)
                CombatVFXManager.Instance.SpawnHitEffects(ctx, targets);
        }

        /// <summary>
        /// Spawns hit effects on empty traversal tiles when the ability supports firing
        /// without targets (e.g. Barrage hitting open ground).
        /// No-op if canExecuteWithoutTargets is false or no HitEffectPrefab is set.
        /// </summary>
        private void SpawnHitEffectsOnTraversalTiles(AbilityContext ctx)
        {
            if (!ctx.ability.canExecuteWithoutTargets) return;
            if (CombatVFXManager.Instance == null) return;

            var traversalTiles = ctx.ability.targeting.GetTraversal(ctx);
            CombatVFXManager.Instance.SpawnHitEffectsOnEmptyTiles(ctx, traversalTiles);
        }

        #endregion

        #region Animation Helpers

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
                    if (targetAnimator.HasState("Buff")) targetAnimator.PlayAnimation("Buff");
                    else targetAnimator.PlayHurt();
                    break;
                case TargetAnimationType.Debuff:
                    if (targetAnimator.HasState("Debuff")) targetAnimator.PlayAnimation("Debuff");
                    else targetAnimator.PlayHurt();
                    break;
                case TargetAnimationType.Knockback:
                    targetAnimator.PlayKnockbackStart();
                    break;
                case TargetAnimationType.Movement:
                    targetAnimator.PlayMove();
                    break;
            }
        }

        #endregion

        #region Predicates

        private bool HasChargeEffect(Ability ability) =>
            ability?.effects?.Any(e => e is ChargeEffect) ?? false;

        private bool HasSelfKnockbackEffect(Ability ability)
        {
            if (ability?.effects == null) return false;
            return ability.effects.Any(e => e is KnockbackEffect kb && kb.applyToSelf);
        }

        private bool HasBuffEffects(StatusEffect statusEffect)
        {
            foreach (var sa in statusEffect.statusesToApply)
            {
                if (IsBuffEffectType(sa.statusEffectData.effectType))
                    return true;
            }
            return false;
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

        #endregion

        #region Cleanup

        private void ClearContext()
        {
            CurrentAbilityContext = null;

            if (unitAnimator != null && !unitAnimator.IsInKnockbackSequence)
                StartCoroutine(ReturnToNaturalFacingDelayed(0.1f));
        }

        private IEnumerator ReturnToNaturalFacingDelayed(float delay)
        {
            yield return new WaitForSeconds(delay);
            unit.ReturnToNaturalFacing();
        }

        #endregion
    }
}