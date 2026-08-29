using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class AbilitySequencer : MonoBehaviour
    {
        public AbilityContext CurrentAbilityContext { get; private set; }
        
        private Unit unit;
        private UnitAnimator unitAnimator;
        private CameraController cameraController;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogging = false;
        
        void Awake()
        {
            unit = GetComponent<Unit>();
            unitAnimator = GetComponentInChildren<UnitAnimator>();
        }

        void Start()
        {
            cameraController = FindAnyObjectByType<CameraController>();
        }
        
        public void BeginSequence(AbilityContext ctx, List<Unit> targets)
        {
            StartCoroutine(RunSequence(ctx, targets));
        }


        private IEnumerator RunSequence(AbilityContext ctx, List<Unit> targets)
        {
            CurrentAbilityContext = ctx;
            unit.FaceDirection(ctx.aimDir);

            bool isFollowing = ctx.ability.cameraMode == CameraMode.Follow && cameraController != null;

            if (!string.IsNullOrEmpty(ctx.ability.AnimationState) && unitAnimator != null)
                yield return StartCoroutine(ExecuteTimedEffects(ctx, targets));
            else
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
            
            if (!string.IsNullOrEmpty(ctx.ability.AnimationHoldState) && unitAnimator != null)
                BindHoldToGrantedStatusEffect(ctx);
            
            if (UnitDownedSequencer.Instance != null)
            {
                Unit returnTo = (ctx.caster != null && !ctx.caster.IsDead) ? ctx.caster : null;
                yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue(returnTo));
            }

            if (isFollowing)
                cameraController.EndFollowing();

            ClearContext();
        }

        private IEnumerator ExecuteTimedEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (enableDebugLogging)
                Debug.Log($"[AbilitySequencer:{unit.name}] Starting timed effects for {ctx.ability.AnimationState}" +
                          $"{(unitAnimator.IsHurtIdle && !string.IsNullOrEmpty(ctx.ability.AnimationStateHurt) ? $" (hurt override: {ctx.ability.AnimationStateHurt})" : "")}");
            
            if (cameraController != null)
            {
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));
            }

            if (ctx.ability.cameraMode == CameraMode.Follow && cameraController != null)
                cameraController.BeginFollowing(unit);
            
            var animator = unitAnimator?.GetComponent<Animator>();
            if (animator == null)
            {
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
                yield break;
            }
            
            yield return StartCoroutine(ProcessWarnedDodges(ctx, targets));

            string resolvedAnimState = ctx.ability.AnimationState;
            if (!string.IsNullOrEmpty(ctx.ability.AnimationState))
            {
                bool useHurtAnim = unitAnimator.IsHurtIdle
                    && !string.IsNullOrEmpty(ctx.ability.AnimationStateHurt)
                    && unitAnimator.HasState(ctx.ability.AnimationStateHurt);

                resolvedAnimState = useHurtAnim
                    ? ctx.ability.AnimationStateHurt
                    : ctx.ability.AnimationState;

                if (!string.IsNullOrEmpty(ctx.ability.AnimationHoldState))
                {
                    unitAnimator.PlayAnimationThenHold(
                        resolvedAnimState,
                        ctx.ability.AnimationHoldState,
                        ctx.ability.AnimationHoldReleaseState);
                }
                else
                {
                    unitAnimator.PlayAnimation(resolvedAnimState);
                }
            }

            bool castEffectTriggered = false;
            void OnCastEffect()
            {
                if (castEffectTriggered) return;
                castEffectTriggered = true;
                if (enableDebugLogging) Debug.Log("[AbilitySequencer] AnimEvent_CastEffect fired");
                SpawnCastEffect(ctx);
            }
            unitAnimator.OnCastEffectEvent += OnCastEffect;
            
            var midAnimFiredEffects = new HashSet<AbilityEffect>();
            void OnAbilityEffect(int slot)
            {
                if (ctx.ability.cameraMode != CameraMode.None) return;

                var slotEffects = ctx.ability.effects
                    .Where(e => e.midAnimationEventIndex == slot)
                    .ToList();

                if (slotEffects.Count == 0)
                {
                    if (enableDebugLogging)
                        Debug.LogWarning($"[AbilitySequencer] AnimEvent_AbilityEffect{slot} fired but no effects have midAnimationEventIndex = {slot}");
                    return;
                }

                if (enableDebugLogging)
                    Debug.Log($"[AbilitySequencer] AnimEvent_AbilityEffect{slot} fired — applying {slotEffects.Count} effect(s)");
                
                bool hasDamageEffect = slotEffects.Any(e => e.AnimationPhase == EffectAnimationPhase.Damage);
                if (hasDamageEffect)
                {
                    foreach (var target in targets)
                        PlayTargetAnimation(target, "Hurt");
                }

                foreach (var effect in slotEffects)
                {
                    effect.Apply(ctx, targets);
                    SpawnHitEffects(ctx, targets);
                    midAnimFiredEffects.Add(effect);
                }
            }
            unitAnimator.OnAbilityEffectEvent += OnAbilityEffect;

            float waitTime = 0f;
            while (waitTime < 1f && !animator.GetCurrentAnimatorStateInfo(0).IsName(resolvedAnimState))
            {
                yield return null;
                waitTime += Time.deltaTime;
            }

            if (!animator.GetCurrentAnimatorStateInfo(0).IsName(resolvedAnimState))
            {
                Debug.LogWarning($"[AbilitySequencer] Animation {resolvedAnimState} never started, using immediate effects");
                unitAnimator.OnCastEffectEvent -= OnCastEffect;
                unitAnimator.OnAbilityEffectEvent -= OnAbilityEffect;
                yield return StartCoroutine(ExecuteImmediateEffects(ctx, targets));
                yield break;
            }
            
            while (true)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (!stateInfo.IsName(resolvedAnimState)) break;
                if (stateInfo.normalizedTime >= 1.0f && !stateInfo.loop) break;
                yield return null;
            }

            unitAnimator.OnCastEffectEvent -= OnCastEffect;
            unitAnimator.OnAbilityEffectEvent -= OnAbilityEffect;

            if (!castEffectTriggered)
            {
                if (enableDebugLogging) Debug.Log("[AbilitySequencer] AnimEvent_CastEffect fallback triggered");
                SpawnCastEffect(ctx);
            }

            if (HasChargeEffect(ctx.ability))
            {
                yield return StartCoroutine(ExecuteChargeSequence(ctx, targets));
                yield break;
            }
            
            yield return StartCoroutine(HandlePostAnimationEffects(ctx, targets, midAnimFiredEffects));
        }

        private IEnumerator HandlePostAnimationEffects(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            if (HasSelfKnockbackEffect(ctx.ability))
                StartCoroutine(HandleSelfKnockbackEffects(ctx));

            var nonSelfTargets = targets.Where(t => t != unit).ToList();
            if (nonSelfTargets.Count > 0)
                yield return StartCoroutine(HandleCameraTransitionsAndEffects(ctx, nonSelfTargets, midAnimFiredEffects));
            else if (ctx.ability.canExecuteWithoutTargets)
            {
                foreach (var effect in ctx.ability.effects)
                {
                    if (effect.AnimationPhase == EffectAnimationPhase.PreEffect) continue;

                    if (effect.AnimationPhase == EffectAnimationPhase.PostEffect) continue;
                    if (effect is ChargeEffect) continue;

                    if (midAnimFiredEffects != null && midAnimFiredEffects.Contains(effect)) continue;
                    yield return StartCoroutine(ApplyEffectWithOptionalTeleportCamera(ctx, targets, effect));
                }
                SpawnHitEffectsOnTraversalTiles(ctx);
            }

            yield return StartCoroutine(HandleRemainingEffects(ctx, targets, midAnimFiredEffects));
        }

        private IEnumerator ExecuteChargeSequence(AbilityContext ctx, List<Unit> targets)
        {
            var chargeEffect = ctx.ability.effects.OfType<ChargeEffect>().FirstOrDefault();
            if (chargeEffect == null) yield break;

            yield return StartCoroutine(chargeEffect.ExecuteCharge(ctx, targets, unitAnimator));
            yield return StartCoroutine(HandleRemainingEffects(ctx, targets));
        }

        private IEnumerator HandleCameraTransitionsAndEffects(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            if (cameraController == null)
            {
                yield return StartCoroutine(ApplyDamageEffectsWithHurtAnimation(ctx, targets, midAnimFiredEffects));
                SpawnHitEffects(ctx, targets);
                yield break;
            }

            CameraMode cameraMode = ctx.ability.cameraMode;

            if (cameraMode == CameraMode.None)
            {
                ApplyAbilityEffectsToTargets(ctx, targets, midAnimFiredEffects);
                SpawnHitEffects(ctx, targets);
                SpawnHitEffectsOnTraversalTiles(ctx);
                yield break;
            }

            bool shouldUseTransitions = cameraMode == CameraMode.Targeted || cameraMode == CameraMode.PreExecutionZoom;
            bool hasMovementEffect = ctx.ability.effects.Any(e =>
                e.AnimationPhase == EffectAnimationPhase.Displacement && !e.IsSelfOnly(ctx));

            if (!shouldUseTransitions && !hasMovementEffect)
            {
                yield return StartCoroutine(ApplyDamageEffectsWithHurtAnimation(ctx, targets, midAnimFiredEffects));
                SpawnHitEffects(ctx, targets);
                SpawnHitEffectsOnTraversalTiles(ctx);
                yield break;
            }

            yield return StartCoroutine(HandleSingleTargetingEffects(ctx, targets));
        }
        
        private IEnumerator ApplyDamageEffectsWithHurtAnimation(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            var damageEffects = ctx.ability.effects
                .Where(e => e.AnimationPhase == EffectAnimationPhase.Damage
                            && !(midAnimFiredEffects != null && midAnimFiredEffects.Contains(e)))
                .ToList();

            bool hasDamage = damageEffects.Count > 0;

            if (hasDamage)
            {
                foreach (var effect in damageEffects)
                    effect.Apply(ctx, targets);

                foreach (var target in targets)
                {
                    if (target == null) continue;
                    var targetAnimator  = target.GetComponentInChildren<UnitAnimator>();
                    var targetHealthBar = target.GetComponentInChildren<UnitHealthBarDisplay>();

                    if (target.IsDead)
                    {
                        if (UnitDownedSequencer.Instance != null)
                            yield return StartCoroutine(UnitDownedSequencer.Instance.PresentDownedInline(target));
                    }
                    else
                    {
                        PlayTargetAnimation(target, "Hurt");

                        Coroutine hurtWait   = targetAnimator  != null ? StartCoroutine(targetAnimator.WaitForHurtAnimation())  : null;
                        Coroutine healthWait = targetHealthBar != null ? StartCoroutine(targetHealthBar.WaitForTweenComplete()) : null;

                        if (hurtWait   != null) yield return hurtWait;
                        if (healthWait != null) yield return healthWait;
                    }
                }
            }
            
            foreach (var effect in ctx.ability.effects)
            {
                if (effect.AnimationPhase == EffectAnimationPhase.PreEffect)  continue;
                if (effect.AnimationPhase == EffectAnimationPhase.Damage)     continue; 
                if (effect.IsSelfOnly(ctx))                                   continue;
                if (midAnimFiredEffects != null && midAnimFiredEffects.Contains(effect)) continue;

                effect.Apply(ctx, targets);
            }
        }

        private IEnumerator HandleSingleTargetingEffects(AbilityContext ctx, List<Unit> targets)
        {
            bool wideFraming = ctx.ability.cameraMode == CameraMode.PreExecutionZoom;

            foreach (var target in targets)
            {
                if (target == null) continue;

                yield return StartCoroutine(cameraController.TransitionTo(
                    GetFocusPositionForTarget(target, wideFraming)));

                yield return StartCoroutine(PlayTargetEffectsWithAnimation(ctx, new List<Unit> { target }));
                
                if (!target.IsDead)
                    yield return new WaitForSeconds(0.3f);
            }
            
            yield return StartCoroutine(cameraController.TransitionTo(
                cameraController.UnitFocusPosition(unit)));
        }

        private Vector3 GetFocusPositionForTarget(Unit target, bool wideFraming)
        {
            if (!wideFraming || unit == null)
                return cameraController.UnitFocusPosition(target);

            Vector3 midpoint = (unit.transform.position + target.transform.position) / 2f;
            float radius = Vector3.Distance(unit.transform.position, target.transform.position) / 2f;

            return cameraController.FitRadius(midpoint, radius);
        }

        private IEnumerator PlayTargetEffectsWithAnimation(AbilityContext ctx, List<Unit> targets)
        {
            var displacementEffects = ctx.ability.effects
                .Where(e => e.AnimationPhase == EffectAnimationPhase.Displacement && !e.IsSelfOnly(ctx))
                .ToList();
            var damageEffects = ctx.ability.effects
                .Where(e => e.AnimationPhase == EffectAnimationPhase.Damage)
                .ToList();
            var statusEffects = ctx.ability.effects
                .Where(e => e.AnimationPhase == EffectAnimationPhase.StatusBuff ||
                            e.AnimationPhase == EffectAnimationPhase.StatusDebuff)
                .ToList();
            var postEffects = ctx.ability.effects
                .Where(e => e.AnimationPhase == EffectAnimationPhase.PostEffect)
                .ToList();
            
            bool hasDisplacement = displacementEffects.Count > 0;
            if (hasDisplacement)
            {
                foreach (var target in targets)
                {
                    var hint = displacementEffects[0].TargetAnimationHint;
                    if (hint != null) PlayTargetAnimation(target, hint);
                }
                foreach (var effect in displacementEffects) effect.Apply(ctx, targets);

                float dispDuration = displacementEffects.Max(e => e.ExpectedAnimationDuration);
                if (dispDuration > 0f) yield return new WaitForSeconds(dispDuration);
            }

            if (!hasDisplacement && damageEffects.Count > 0)
            {
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects) effect.Apply(ctx, targets);

                foreach (var target in targets)
                {
                    if (target == null) continue;
                    var targetAnimator  = target.GetComponentInChildren<UnitAnimator>();
                    var targetHealthBar = target.GetComponentInChildren<UnitHealthBarDisplay>();

                    if (target.IsDead)
                    {
                        if (UnitDownedSequencer.Instance != null)
                            yield return StartCoroutine(UnitDownedSequencer.Instance.PresentDownedInline(target));
                    }
                    else
                    {
                        PlayTargetAnimation(target, "Hurt");

                        Coroutine hurtWait   = targetAnimator  != null ? StartCoroutine(targetAnimator.WaitForHurtAnimation())  : null;
                        Coroutine healthWait = targetHealthBar != null ? StartCoroutine(targetHealthBar.WaitForTweenComplete()) : null;

                        if (hurtWait   != null) yield return hurtWait;
                        if (healthWait != null) yield return healthWait;
                    }
                }
            }
            else if (damageEffects.Count > 0)
            {
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects) effect.Apply(ctx, targets);
            }

            var buffStatusEffects   = statusEffects.Where(se => se.IsBuffEffect).ToList();
            var debuffStatusEffects = statusEffects.Where(se => !se.IsBuffEffect).ToList();

            if (buffStatusEffects.Count > 0)
            {
                foreach (var target in targets) PlayTargetAnimation(target, "Buff");
                foreach (var effect in buffStatusEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.6f);
            }

            if (debuffStatusEffects.Count > 0)
            {
                foreach (var target in targets) PlayTargetAnimation(target, "Debuff");
                foreach (var effect in debuffStatusEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.6f);
            }

            if (postEffects.Count > 0)
            {
                foreach (var effect in postEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.5f);
            }
        }

        private IEnumerator HandleRemainingEffects(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            foreach (var effect in ctx.ability.effects)
            {
                if (effect.AnimationPhase != EffectAnimationPhase.PostEffect) continue;

                if (effect is ChargeEffect) continue;
                
                if (midAnimFiredEffects != null && midAnimFiredEffects.Contains(effect)) continue;

                var selfHint = effect.SelfCastAnimationHint;
                if (selfHint != null)
                {
                    effect.Apply(ctx, new List<Unit> { unit });
                    PlayTargetAnimation(unit, selfHint);
                    yield return new WaitForSeconds(0.6f);
                }
                else
                {
                    yield return StartCoroutine(ApplyEffectWithOptionalTeleportCamera(ctx, targets, effect));
                }
            }
        }

        private IEnumerator HandleSelfKnockbackEffects(AbilityContext ctx)
        {
            var preEffects = ctx.ability.effects
                .Where(e => e.AnimationPhase == EffectAnimationPhase.PreEffect)
                .ToList();
            if (preEffects.Count == 0) yield break;

            var selfTargetList = new List<Unit> { unit };
            foreach (var effect in preEffects)
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

        private void ApplyAbilityEffectsToTargets(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            foreach (var effect in ctx.ability.effects)
            {
                if (effect.AnimationPhase == EffectAnimationPhase.PreEffect) continue;

                if (effect.IsSelfOnly(ctx)) continue;

                if (midAnimFiredEffects != null && midAnimFiredEffects.Contains(effect)) continue;

                effect.Apply(ctx, targets);
            }
        }

        private IEnumerator ApplyEffectWithOptionalTeleportCamera(
            AbilityContext ctx,
            List<Unit> targets,
            AbilityEffect effect)
        {
            if (effect == null) yield break;

            if (cameraController != null && effect.NeedsCameraPreview(ctx, out var focusTile))
            {
                Vector3 focusPosition = cameraController.WorldFocusPosition(focusTile.transform.position);
                yield return StartCoroutine(cameraController.TransitionTo(focusPosition, 0.5f));
            }

            effect.Apply(ctx, targets);
        }

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
        
        private void SpawnHitEffectsOnTraversalTiles(AbilityContext ctx)
        {
            if (!ctx.ability.canExecuteWithoutTargets) return;
            if (CombatVFXManager.Instance == null) return;

            var traversalTiles = ctx.ability.targeting.GetTraversal(ctx);
            CombatVFXManager.Instance.SpawnHitEffectsOnEmptyTiles(ctx, traversalTiles);
        }

        private void PlayTargetAnimation(Unit target, string hint)
        {
            if (target == null || hint == null) return;
            var targetAnimator = target.GetComponentInChildren<UnitAnimator>();
            if (targetAnimator == null) return;

            switch (hint)
            {
                case "Hurt":
                    if (IsImmuneToDamageReaction(target)) break;
                    targetAnimator.PlayHurt();
                    break;
                case "Buff":
                    if (targetAnimator.HasState("Buff")) targetAnimator.PlayAnimation("Buff");
                    else targetAnimator.PlayHurt();
                    break;
                case "Debuff":
                    if (targetAnimator.HasState("Debuff")) targetAnimator.PlayAnimation("Debuff");
                    else targetAnimator.PlayHurt();
                    break;
                case "Knockback":
                    targetAnimator.PlayKnockbackStart();
                    break;
                case "Movement":
                    targetAnimator.PlayMove();
                    break;
                default:
                    if (targetAnimator.HasState(hint)) targetAnimator.PlayAnimation(hint);
                    break;
            }
        }
        
        private bool IsImmuneToDamageReaction(Unit target)
        {
            if (StatusEffectManager.Instance == null) return false;

            return StatusEffectManager.Instance.HasStatusEffect(target, StatusEffectType.Invulnerable)
                || StatusEffectManager.Instance.HasStatusEffect(target, StatusEffectType.Shielded);
        }

        private bool HasChargeEffect(Ability ability) =>
            ability?.effects?.Any(e => e is ChargeEffect) ?? false;

        private bool HasSelfKnockbackEffect(Ability ability)
        {
            if (ability?.effects == null) return false;
            return ability.effects.Any(e => e.AnimationPhase == EffectAnimationPhase.PreEffect);
        }
        
        private IEnumerator ProcessWarnedDodges(AbilityContext ctx, List<Unit> targets)
        {
            if (StatusEffectManager.Instance == null) yield break;

            foreach (var target in targets.ToList())
            {
                if (target == null) continue;

                bool targetIsAlly = target.IsAllyOf(ctx.caster);
                if (targetIsAlly) continue;

                var warned = StatusEffectManager.Instance.GetStatusEffect(target, StatusEffectType.Warned);
                if (warned == null) continue;

                warned.wasTriggered = true;
                StatusEffectManager.Instance.RemoveStatusEffect(target, warned);

                var (dodgeTile, dodgePath) = FindWalkDodgeTile(target, ctx);

                if (dodgeTile == null)
                {
                    targets.Remove(target);
                    Debug.Log($"[Warned] {target.name} is fully blocked — damage negated, unit stays.");
                    continue;
                }
                
                if (cameraController != null)
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(target)));

                if (enableDebugLogging)
                    Debug.Log($"[Warned] {target.name} walking to {dodgeTile.gridPosition} ({dodgePath.Count} steps)");
                
                if (UnitMovementController.Instance != null)
                    yield return StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                        target,
                        dodgeTile,
                        waypoints: dodgePath,
                        followCameraForAI: true));

                if (cameraController != null)
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(unit)));

                targets.Remove(target);
                Debug.Log($"[Warned] {target.name} successfully walked out of range.");
            }
        }
        
        private (Tile tile, List<Tile> path) FindWalkDodgeTile(Unit target, AbilityContext ctx)
        {
            if (target?.currentTile == null) return (null, null);

            var dangerTiles = new HashSet<Tile>(ctx.ability.targeting.GetTraversal(ctx));

            Vector3 awayFromAttacker = Vector3.zero;
            if (ctx.caster?.currentTile != null)
            {
                awayFromAttacker = (target.currentTile.transform.position
                                  - ctx.caster.currentTile.transform.position).normalized;
            }

            var cameFrom   = new Dictionary<Tile, Tile>();
            var queue      = new Queue<Tile>();
            var visited    = new HashSet<Tile>();

            queue.Enqueue(target.currentTile);
            visited.Add(target.currentTile);
            cameFrom[target.currentTile] = null;

            int       bestDepth     = int.MaxValue;
            var       safeCandidates = new List<Tile>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                int depth = 0;
                {
                    var step = current;
                    while (cameFrom[step] != null) { step = cameFrom[step]; depth++; }
                }

                if (depth > bestDepth) continue;

                if (current != target.currentTile && !dangerTiles.Contains(current))
                {
                    if (depth < bestDepth)
                    {
                        bestDepth = depth;
                        safeCandidates.Clear();
                    }
                    safeCandidates.Add(current);
                    continue;
                }

                foreach (var neighbour in GridManager.Instance.GetAdjacentTiles(current, includeDiagonals: false))
                {
                    if (neighbour == null || visited.Contains(neighbour)) continue;
                    if (!neighbour.passableTerrain || neighbour.occupied)           continue;

                    visited.Add(neighbour);
                    cameFrom[neighbour] = current;
                    queue.Enqueue(neighbour);
                }
            }

            if (safeCandidates.Count == 0) return (null, null);

            Tile best = safeCandidates[0];
            float bestScore = float.MinValue;
            foreach (var candidate in safeCandidates)
            {
                Vector3 toCandidate = (candidate.transform.position
                                     - target.currentTile.transform.position).normalized;
                float score = Vector3.Dot(toCandidate, awayFromAttacker);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }

            var path = new List<Tile>();
            var cursor = best;
            while (cameFrom[cursor] != null)
            {
                path.Add(cursor);
                cursor = cameFrom[cursor];
            }
            path.Reverse();

            return (best, path);
        }

        private void BindHoldToGrantedStatusEffect(AbilityContext ctx)
        {
            if (StatusEffectManager.Instance == null || ctx.caster == null) return;

            foreach (var effect in ctx.ability.effects)
            {
                if (effect is not StatusEffect statusEffect) continue;

                foreach (var application in statusEffect.statusesToApply)
                {
                    if (application.statusEffectData == null) continue;
                    if (application.applyTo != StatusEffect.ApplicationTarget.Caster &&
                        application.applyTo != StatusEffect.ApplicationTarget.Both) continue;

                    var instance = StatusEffectManager.Instance.GetStatusEffect(
                        ctx.caster, application.statusEffectData.effectType);

                    if (instance != null)
                    {
                        unitAnimator.BindHoldToStatusEffect(instance);
                        return;
                    }
                }
            }
        }

        private void ClearContext()
        {
            CurrentAbilityContext = null;
        }
    }

}