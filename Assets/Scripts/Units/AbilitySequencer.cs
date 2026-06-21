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

            // Present any units that died during this ability's effects one at a time,
            // immediately after the ability resolves — before control returns to the caster.
            // If the caster is still alive, pass them as the return unit so the camera pans
            // back to them after all death animations finish.
            // If the caster died (e.g. recoil damage), pass null — the camera stays on the
            // last death position; the next turn's StartNextTurn pan handles the transition.
            if (UnitDeathSequencer.Instance != null)
            {
                Unit returnTo = (ctx.caster != null && !ctx.caster.IsDead) ? ctx.caster : null;
                yield return StartCoroutine(UnitDeathSequencer.Instance.DrainDeathQueue(returnTo));
            }

            ClearContext();
        }

        private IEnumerator ExecuteTimedEffects(AbilityContext ctx, List<Unit> targets)
        {
            if (enableDebugLogging)
                Debug.Log($"[AbilitySequencer:{unit.name}] Starting timed effects for {ctx.ability.AnimationState}" +
                          $"{(unitAnimator.IsHurtIdle && !string.IsNullOrEmpty(ctx.ability.AnimationStateHurt) ? $" (hurt override: {ctx.ability.AnimationStateHurt})" : "")}");

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

            // STEP 2.5: Process Warned dodges before the animation plays.
            // Any target carrying Warned gets a chance to step to an adjacent free tile.
            // If their new tile is outside the ability's traversal the attack misses them;
            // they are removed from the targets list so no effects land on them.
            yield return StartCoroutine(ProcessWarnedDodges(ctx, targets));

            // STEP 3: Play animation — use the hurt variant when the caster is in the hurt idle
            // tier and the ability has a hurt animation configured; fall back to the standard state.
            string resolvedAnimState = ctx.ability.AnimationState;
            if (!string.IsNullOrEmpty(ctx.ability.AnimationState))
            {
                bool useHurtAnim = unitAnimator.IsHurtIdle
                    && !string.IsNullOrEmpty(ctx.ability.AnimationStateHurt)
                    && unitAnimator.HasState(ctx.ability.AnimationStateHurt);

                resolvedAnimState = useHurtAnim
                    ? ctx.ability.AnimationStateHurt
                    : ctx.ability.AnimationState;

                unitAnimator.PlayAnimation(resolvedAnimState);
            }

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

            // STEP 4.5: When suppressCameraTransitions is set, subscribe to mid-animation
            // effect events. Each slot fires the matching effects immediately as the
            // animation clip reaches that frame, rather than batching everything post-animation.
            // Track which effects have already fired so HandleRemainingEffects skips them.
            var midAnimFiredEffects = new HashSet<AbilityEffect>();
            void OnAbilityEffect(int slot)
            {
                if (!ctx.ability.suppressCameraTransitions) return;

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

                foreach (var effect in slotEffects)
                {
                    effect.Apply(ctx, targets);
                    SpawnHitEffects(ctx, targets);
                    midAnimFiredEffects.Add(effect);
                }
            }
            unitAnimator.OnAbilityEffectEvent += OnAbilityEffect;

            // STEP 5: Charge abilities bypass the wait-for-animation-start logic
            if (HasChargeEffect(ctx.ability))
            {
                yield return StartCoroutine(ExecuteChargeSequence(ctx, targets));
                unitAnimator.OnCastEffectEvent -= OnCastEffect;
                unitAnimator.OnAbilityEffectEvent -= OnAbilityEffect;
                if (!castEffectTriggered) SpawnCastEffect(ctx);
                yield break;
            }

            // Non-charge: wait for the animation to actually start
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

            // STEP 6: Wait for the animation to finish
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

            // STEP 7: Post-animation — camera pan + effects
            yield return StartCoroutine(HandlePostAnimationEffects(ctx, targets, midAnimFiredEffects));
        }

        private IEnumerator HandlePostAnimationEffects(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            if (HasSelfKnockbackEffect(ctx.ability))
                StartCoroutine(HandleSelfKnockbackEffects(ctx));

            var nonSelfTargets = targets.Where(t => t != unit).ToList();
            if (nonSelfTargets.Count > 0)
                yield return StartCoroutine(HandleCameraTransitionsAndEffects(ctx, nonSelfTargets));
            else if (ctx.ability.canExecuteWithoutTargets)
            {
                // No units hit but the ability can still fire.
                // Apply all non-PreEffect effects via the camera-preview path so that
                // caster-targeting effects (e.g. TeleportEffect, which reads from ctx
                // rather than the targets list) are executed correctly.
                // SpawnHitEffectsOnTraversalTiles is called after so VFX still appear.
                foreach (var effect in ctx.ability.effects)
                {
                    if (effect.AnimationPhase == EffectAnimationPhase.PreEffect) continue;
                    // PostEffect phase is handled unconditionally by HandleRemainingEffects below.
                    if (effect.AnimationPhase == EffectAnimationPhase.PostEffect) continue;
                    if (effect is ChargeEffect) continue;
                    // Skip effects already dispatched mid-animation.
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

            // suppressCameraTransitions overrides everything — apply effects immediately
            // without panning away from the caster, regardless of targeting type or
            // whether a displacement effect is present (e.g. WiringFaultEffect manages
            // its own animated sequence internally and must not be double-triggered here).
            if (ctx.ability.suppressCameraTransitions)
            {
                ApplyAbilityEffectsToTargets(ctx, targets);
                SpawnHitEffects(ctx, targets);
                SpawnHitEffectsOnTraversalTiles(ctx);
                yield break;
            }

            bool shouldUseTransitions = ctx.ability.targeting.UsesCameraTransitionsPerTarget;
            bool hasMovementEffect = ctx.ability.effects.Any(e =>
                e.AnimationPhase == EffectAnimationPhase.Displacement && !e.IsSelfOnly(ctx));

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
            bool anyTargetDied = false;

            foreach (var target in targets)
            {
                if (target == null) continue;

                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(target)));

                yield return StartCoroutine(PlayTargetEffectsWithAnimation(ctx, new List<Unit> { target }));

                if (target.IsDead)
                {
                    // This target died — DrainDeathQueue in RunSequence will pan to them
                    // and play the death animation from here. Don't pan back to the caster
                    // now or the camera will bounce: caster → dead target → caster → dead target.
                    anyTargetDied = true;
                }
                else
                {
                    yield return new WaitForSeconds(0.3f);
                }
            }

            // Only return to the caster here if no targets died. If any died, DrainDeathQueue
            // will return to the caster after the last death animation finishes.
            if (!anyTargetDied)
            {
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));
            }
        }

        private IEnumerator PlayTargetEffectsWithAnimation(AbilityContext ctx, List<Unit> targets)
        {
            // Group effects by their declared phase — no concrete-type inspection needed.
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

            // --- Displacement (Knockback / Movement / Teleport) ---
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

            // --- Damage ---
            if (!hasDisplacement && damageEffects.Count > 0)
            {
                foreach (var target in targets) PlayTargetAnimation(target, "Hurt");
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.8f);
            }
            else if (damageEffects.Count > 0)
            {
                SpawnHitEffects(ctx, targets);
                foreach (var effect in damageEffects) effect.Apply(ctx, targets);
            }

            // --- Status: Buffs then Debuffs ---
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

            // --- PostEffect (QueuedAction, ApplyTileEffect, etc.) ---
            if (postEffects.Count > 0)
            {
                foreach (var effect in postEffects) effect.Apply(ctx, targets);
                yield return new WaitForSeconds(0.5f);
            }
        }

        private IEnumerator HandleRemainingEffects(AbilityContext ctx, List<Unit> targets, HashSet<AbilityEffect> midAnimFiredEffects = null)
        {
            // Phases that have already been applied earlier in the sequence.
            // Effects in these phases are skipped here to avoid double-application.
            // Displacement and Damage are handled by PlayTargetEffectsWithAnimation
            // (or ApplyAbilityEffectsToTargets for non-camera paths).
            // PreEffect (self-knockback/caster-movement) fired before camera transitions.
            // ChargeEffect is handled entirely via ExecuteChargeSequence — it never reaches Apply.
            bool usedCameraTransitions = ctx.ability.targeting.UsesCameraTransitionsPerTarget
                                         && !ctx.ability.suppressCameraTransitions;

            // When suppressCameraTransitions is set, HandleCameraTransitionsAndEffects took
            // the ApplyAbilityEffectsToTargets path, which already applied every non-PreEffect
            // phase. Treat it the same as the camera-transition path so effects aren't double-fired.
            bool effectsAlreadyApplied = usedCameraTransitions || ctx.ability.suppressCameraTransitions;

            foreach (var effect in ctx.ability.effects)
            {
                // Always skip PreEffect here — it ran before camera transitions.
                if (effect.AnimationPhase == EffectAnimationPhase.PreEffect) continue;

                // If effects were already applied (per-target camera transitions, or
                // suppressCameraTransitions took the immediate-apply path), skip
                // Displacement/Damage/Status to avoid double-application.
                if (effectsAlreadyApplied &&
                    effect.AnimationPhase != EffectAnimationPhase.PostEffect) continue;

                // ChargeEffect bypasses Apply entirely; skip it unconditionally.
                if (effect is ChargeEffect) continue;

                // Skip effects that already fired mid-animation via an Animation Event.
                if (midAnimFiredEffects != null && midAnimFiredEffects.Contains(effect)) continue;

                // If this effect has a self-cast component, apply it to the caster and
                // play the appropriate animation. SelfCastAnimationHint returns null when
                // the effect has no caster-targeting portion.
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
            // PreEffect-phase effects that apply to the caster — typically self-knockback.
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
                // Skip PreEffect-phase effects (self-knockback, caster-movement) —
                // they target the caster and are handled separately.
                if (effect.AnimationPhase == EffectAnimationPhase.PreEffect) continue;

                // Skip effects that only apply to the caster (e.g. Caster-only StatusEffect).
                if (effect.IsSelfOnly(ctx)) continue;

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

        private void PlayTargetAnimation(Unit target, string hint)
        {
            if (target == null || hint == null) return;
            var targetAnimator = target.GetComponent<UnitAnimator>();
            if (targetAnimator == null) return;

            switch (hint)
            {
                case "Hurt":
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

        #endregion

        #region Predicates

        private bool HasChargeEffect(Ability ability) =>
            ability?.effects?.Any(e => e is ChargeEffect) ?? false;

        private bool HasSelfKnockbackEffect(Ability ability)
        {
            if (ability?.effects == null) return false;
            return ability.effects.Any(e => e.AnimationPhase == EffectAnimationPhase.PreEffect);
        }

        /// <summary>
        /// For each target carrying the Warned status, attempts to walk them to the closest
        /// tile outside the full ability traversal before the animation plays.
        /// - If a safe tile is reachable by walking (no jumping), the unit walks there and
        ///   the attack misses them entirely.
        /// - If every path out is physically blocked (walled in), damage is negated and the
        ///   unit stays put — they couldn't escape but the warning still protected them.
        /// </summary>
        private IEnumerator ProcessWarnedDodges(AbilityContext ctx, List<Unit> targets)
        {
            if (StatusEffectManager.Instance == null) yield break;

            // Iterate over a snapshot so we can safely modify `targets` mid-loop.
            foreach (var target in targets.ToList())
            {
                if (target == null) continue;

                // A unit only dodges INCOMING ATTACKS, never friendly buffs.
                // If the target is an ally of the caster (same faction), skip the dodge —
                // this prevents Heads Up from causing the warned ally to dodge the buff itself.
                bool targetIsAlly = (target is EnemyUnit) == (ctx.caster is EnemyUnit);
                if (targetIsAlly) continue;

                var warned = StatusEffectManager.Instance.GetStatusEffect(target, StatusEffectType.Warned);
                if (warned == null) continue;

                // Mark as triggered — the unit reacted to the warning regardless of outcome.
                warned.wasTriggered = true;
                StatusEffectManager.Instance.RemoveStatusEffect(target, warned);

                // Find the closest safe tile reachable by walking (no jumping).
                // Returns null only when the unit is completely walled in.
                var (dodgeTile, dodgePath) = FindWalkDodgeTile(target, ctx);

                if (dodgeTile == null)
                {
                    // Completely blocked — negate the damage by removing from targets.
                    // The unit stays put but the warning still protected them.
                    targets.Remove(target);
                    Debug.Log($"[Warned] {target.name} is fully blocked — damage negated, unit stays.");
                    continue;
                }

                // Pan camera to the warned unit so the walk is visible.
                if (cameraController != null)
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(target)));

                if (enableDebugLogging)
                    Debug.Log($"[Warned] {target.name} walking to {dodgeTile.gridPosition} ({dodgePath.Count} steps)");

                // Walk the unit along the path using the standard movement system.
                // followCameraForAI=true so the camera tracks the unit during the walk.
                if (UnitMovementController.Instance != null)
                    yield return StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                        target,
                        dodgeTile,
                        waypoints: dodgePath,
                        followCameraForAI: true));

                // Pan camera back to the caster ready for the attack animation.
                if (cameraController != null)
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(unit)));

                // The dodge tile was chosen because it is outside the traversal, so the
                // attack always misses — remove the target from the list.
                targets.Remove(target);
                Debug.Log($"[Warned] {target.name} successfully walked out of range.");
            }
        }

        /// <summary>
        /// BFS outward from the warned unit's tile to find the closest tile that is both:
        ///   • outside the full ability traversal (danger zone), and
        ///   • reachable by walking — no jumping, no passing through occupied tiles.
        ///
        /// Among tiles at equal BFS distance, the one whose direction from the unit is
        /// furthest from the attacker is preferred.
        ///
        /// Returns (null, empty) only when the unit is completely walled in with no
        /// walkable path to any safe tile at all.
        /// </summary>
        private (Tile tile, List<Tile> path) FindWalkDodgeTile(Unit target, AbilityContext ctx)
        {
            if (target?.currentTile == null) return (null, null);

            // Snapshot the full traversal — every tile the ability can reach is dangerous.
            var dangerTiles = new HashSet<Tile>(ctx.ability.targeting.GetTraversal(ctx));

            // Pre-compute the direction away from the attacker for tiebreaking.
            // Higher dot product with this vector = further from attacker = preferred.
            Vector3 awayFromAttacker = Vector3.zero;
            if (ctx.caster?.currentTile != null)
            {
                awayFromAttacker = (target.currentTile.transform.position
                                  - ctx.caster.currentTile.transform.position).normalized;
            }

            // BFS — no step limit, walk only (occupied tiles block the path).
            // We track the first-step from the origin for each visited tile so we can
            // reconstruct the full path once we find a safe destination.
            var cameFrom   = new Dictionary<Tile, Tile>();
            var queue      = new Queue<Tile>();
            var visited    = new HashSet<Tile>();

            queue.Enqueue(target.currentTile);
            visited.Add(target.currentTile);
            cameFrom[target.currentTile] = null;

            // Collect ALL safe tiles at the minimum BFS depth found, then tiebreak.
            int       bestDepth     = int.MaxValue;
            var       safeCandidates = new List<Tile>();

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                // Compute BFS depth for this tile.
                int depth = 0;
                {
                    var step = current;
                    while (cameFrom[step] != null) { step = cameFrom[step]; depth++; }
                }

                // If we are already deeper than the best depth found, no point going further.
                if (depth > bestDepth) continue;

                // Is this tile safe (outside the danger zone)?
                // We never dodge to our own current tile.
                if (current != target.currentTile && !dangerTiles.Contains(current))
                {
                    if (depth < bestDepth)
                    {
                        bestDepth = depth;
                        safeCandidates.Clear();
                    }
                    safeCandidates.Add(current);
                    // Do NOT enqueue neighbours — we already have the shortest distance.
                    continue;
                }

                // Expand neighbours (walk only — same Y level, passable, unoccupied).
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

            // Tiebreak: among equally close safe tiles, prefer the one furthest from the attacker.
            Tile best = safeCandidates[0];
            float bestScore = float.MinValue;
            foreach (var candidate in safeCandidates)
            {
                Vector3 toCandidate = (candidate.transform.position
                                     - target.currentTile.transform.position).normalized;
                float score = Vector3.Dot(toCandidate, awayFromAttacker);
                if (score > bestScore) { bestScore = score; best = candidate; }
            }

            // Reconstruct the walk path from origin to best tile.
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

        #endregion

        #region Cleanup

        private void ClearContext()
        {
            CurrentAbilityContext = null;
        }

        #endregion
    }
}