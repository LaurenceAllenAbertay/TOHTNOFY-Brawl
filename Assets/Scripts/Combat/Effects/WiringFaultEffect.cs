using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Wiring Fault — Kallper's signature ability.
    ///
    /// Sequence (all steps are animated and sequential within one coroutine):
    ///   1. Pull  — the first unit in the line is dragged toward the caster as far as
    ///              possible. The hooked unit takes full ability damage on arrival.
    ///   2. Recoil — the caster is knocked back up to 2 tiles (opposite aim direction).
    ///              If the caster is against a wall (zero recoil tiles available) the
    ///              sequence ends after the pull — no recoil, no Shocked applied.
    ///   3. Branch (only reached when recoil occurs):
    ///       • Hit   — if the recoil path ends with a unit immediately beyond the landing
    ///                 tile, that unit takes double ability damage and gains Shocked.
    ///                 The caster is unharmed.
    ///       • Miss  — if nothing is hit, the caster gains Shocked (no damage to self).
    ///
    /// Setup in Unity:
    ///   • Ability targeting          : LineTargeting  (horizontalOnly = true, stopAtFirstUnit = true,
    ///                                  maxTargets = 1)
    ///   • Ability suppressCameraTransitions : true  — keeps the camera on the caster throughout.
    ///   • Ability effects            : [ WiringFaultEffect ]  (this is the only effect needed)
    ///   • shockedStatusData          : assign the Shocked StatusEffectData asset
    ///                                  (triggerTiming = StartOfTurn so damage fires each shocked turn)
    ///   • Ability.AnimationState must be set to Kallper's cast animation clip name.
    ///   • An AnimEvent_CastEffect frame event on that clip triggers the pull.
    ///
    /// Phases:
    ///   AnimationPhase = Displacement — the sequencer runs pre-animation camera pans,
    ///   plays the caster's cast animation, then the AnimEvent_CastEffect Unity Animation
    ///   Event fires OnCastEffectEvent on UnitAnimator, which AbilitySequencer uses to
    ///   call SpawnCastEffect. We take over from there inside our coroutine which is
    ///   started via Apply (called by AbilitySequencer's Displacement phase loop).
    ///
    /// Create via: Assets > Create > TNFY Brawl > Effects > Wiring Fault Effect
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Wiring Fault Effect")]
    public class WiringFaultEffect : AbilityEffect
    {
        // ── Inspector ─────────────────────────────────────────────────────────────

        [Header("Status")]
        [Tooltip("Shocked StatusEffectData asset. Applied to the collision victim on a hit, " +
                 "or to the caster on a miss.")]
        public StatusEffectData shockedStatusData;

        [Tooltip("How many turns Shocked lasts.")]
        public int shockedDuration = 2;

        [Tooltip("Damage dealt to the Shocked unit at the start of each turn they are incapacitated. " +
                 "Passed as effectPower to StatusEffectManager — TriggerEffect reads it for the Shocked case.")]
        public int shockedDamage = 5;

        [Header("Recoil Distance")]
        [Tooltip("How many tiles the caster is knocked back after the pull lands.")]
        public int recoilDistance = 2;

        [Header("Animation Timing")]
        [Tooltip("Time in seconds for the hooked unit to travel one tile toward the caster.")]
        public float pullDurationPerTile = 0.15f;

        [Tooltip("Time to wait after PlayKnockback_Start before movement begins. " +
                 "Used for both the pull and the recoil.")]
        public float knockbackStartDelay = 0.1f;

        [Tooltip("Time to wait after the pulled unit plays Knockback_End. " +
                 "Reduce toward zero for a snappier pull-into-recoil feel.")]
        public float pullKnockbackEndDuration = 0.1f;

        [Tooltip("Brief pause after the hooked unit's knockback end before the recoil begins.")]
        public float pauseAfterPull = 0.0f;

        [Tooltip("Time in seconds for the caster to travel one tile during recoil.")]
        public float recoilDurationPerTile = 0.18f;

        [Tooltip("Duration of Knockback_End animation played on caster after recoil.")]
        public float knockbackEndDuration = 0.4f;

        [Tooltip("How long the caster holds the Knockback_Start pose in the wall case " +
                 "before snapping to Knockback_End (no movement occurs).")]
        public float wallRecoilAnimDuration = 0.3f;

        [Tooltip("Duration to wait after the miss debuff animation plays on the caster.")]
        public float missAnimDuration = 0.5f;

        // ── Phase wiring ──────────────────────────────────────────────────────────

        // Displacement so AbilitySequencer places this in the correct phase bucket.
        // The caster's cast animation has already played when Apply is called here.
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.Displacement;

        // The hooked target plays the Knockback animation (being pulled is visually
        // the same rig as being knocked — it just travels in the opposite direction).
        public override string TargetAnimationHint => "Knockback";

        // Give the sequencer a generous window; our coroutine is fire-and-wait so the
        // sequencer's own yield after Displacement is irrelevant — we block internally.
        public override float ExpectedAnimationDuration => 3f;

        // ── Entry point ───────────────────────────────────────────────────────────

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (ctx?.caster == null) return;

            // targets[0] is the hooked unit resolved by LineTargeting.
            Unit hookedUnit = (targets != null && targets.Count > 0) ? targets[0] : null;
            if (hookedUnit == null || hookedUnit.IsDead) return;

            // Store on ctx so other systems can inspect who was hooked.
            ctx.HookedUnit = hookedUnit;

            // Drive the full sequence as a coroutine on the caster MonoBehaviour.
            ctx.caster.StartCoroutine(RunSequence(ctx, hookedUnit));
        }

        // ── Main coroutine ────────────────────────────────────────────────────────

        private IEnumerator RunSequence(AbilityContext ctx, Unit hookedUnit)
        {
            // ── PHASE 1: Pull ─────────────────────────────────────────────────────
            Tile casterTile = ctx.caster.currentTile;
            if (casterTile == null) yield break;

            Vector2Int recoilDir = new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y);
            List<Tile> recoilPath = CalculatePath(ctx.caster, recoilDir, recoilDistance);

            bool wallCase = recoilPath.Count == 0;

            // Walk from the hooked unit toward the caster to find the farthest tile
            // they can actually reach. In the normal case the caster's tile is included
            // because they will vacate it during the recoil. In the wall case the caster
            // never moves, so we stop before their tile — they are a permanent blocker.
            Vector2Int pullDir = recoilDir; // hooked unit travels in the same direction as the caster's recoil
            Tile pullDestination = CalculatePullDestination(
                hookedUnit.currentTile, casterTile, pullDir, includeCasterTile: !wallCase);

            Debug.Log($"[WiringFault] wallCase={wallCase}, pullDestination={pullDestination?.name}, casterTile={casterTile?.name}");

            yield return ctx.caster.StartCoroutine(PullToCasterTile(ctx, hookedUnit, pullDestination));

            // Deal damage to the hooked unit on arrival.
            if (!hookedUnit.IsDead)
            {
                int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);
                int finalDamage = Mathf.Max(1, baseDamage - hookedUnit.currentDefense);
                hookedUnit.ReceiveDamage(finalDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(hookedUnit, ctx.caster);
                ctx.LastResolvedDamage += finalDamage;
                Debug.Log($"[WiringFault] {hookedUnit.name} took {finalDamage} damage on arrival.");
            }

            // Wall case ends here — no recoil, no Shocked.
            // Still play the knockback animation so the player can see the recoil was blocked.
            if (wallCase)
            {
                Debug.Log("[WiringFault] Caster is against a wall — recoil and Shocked skipped.");
                var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
                casterAnimator?.PlayKnockbackStart();
                yield return new WaitForSeconds(wallRecoilAnimDuration);
                casterAnimator?.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);
                yield break;
            }

            // Brief pause before the recoil.
            yield return new WaitForSeconds(pauseAfterPull);

            // ── PHASE 2: Caster recoil knockback ─────────────────────────────────
            bool isPlayerCaster = ctx.caster is PlayerUnit;
            CombatManager combatManager = null;
            if (isPlayerCaster)
            {
                combatManager = Object.FindAnyObjectByType<CombatManager>();
                if (combatManager != null && combatManager.CurrentActiveUnit == ctx.caster)
                    combatManager.BlockAnimationForEffect();
            }

            yield return ctx.caster.StartCoroutine(
                ApplyRecoilWithAnimation(ctx.caster, recoilDir, recoilPath));

            // ── PHASE 3: Branch — hit or miss ────────────────────────────────────
            // Shocked is only ever applied here, after a recoil has occurred.
            bool collisionHit = false;

            Tile landingTile = recoilPath[recoilPath.Count - 1];
            Tile beyondTile  = GridManager.Instance.GetTileInDirection(landingTile, recoilDir);

            if (beyondTile != null &&
                beyondTile.currentUnit != null &&
                beyondTile.currentUnit != ctx.caster &&
                !beyondTile.currentUnit.IsDead)
            {
                collisionHit = true;
                Unit victim = beyondTile.currentUnit;

                int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);
                int doubleDamage = Mathf.Max(1, (baseDamage - victim.currentDefense) * 2);
                victim.ReceiveDamage(doubleDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(victim, ctx.caster);
                ctx.LastResolvedDamage += doubleDamage;
                Debug.Log($"[WiringFault] Recoil slammed into {victim.name} — {doubleDamage} damage (double).");

                victim.GetComponent<UnitAnimator>()?.PlayHurt();
                ApplyShocked(victim, ctx.caster);
            }

            if (!collisionHit)
            {
                // Miss — caster gets Shocked now that the recoil has landed.
                Debug.Log("[WiringFault] Recoil missed — caster takes Shocked.");
                ApplyShocked(ctx.caster, ctx.caster);

                var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
                if (casterAnimator != null && casterAnimator.HasState("Debuff"))
                    casterAnimator.PlayAnimation("Debuff");
                else
                    casterAnimator?.PlayHurt();

                yield return new WaitForSeconds(missAnimDuration);
            }

            // Restore player input now that the full sequence is done.
            if (isPlayerCaster && combatManager != null &&
                combatManager.CurrentActiveUnit == ctx.caster)
            {
                yield return ctx.caster.StartCoroutine(
                    DelayedInputRestore(combatManager, ctx.caster));
            }
        }

        // ── Pull helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Walks from <paramref name="from"/> one step at a time in <paramref name="pullDir"/>
        /// and returns the farthest tile the hooked unit can reach.
        ///
        /// Rules:
        ///   • Stops at any impassable or occupied tile.
        ///   • When the caster's tile is reached: includes it only if
        ///     <paramref name="includeCasterTile"/> is true (normal case — caster will vacate).
        ///     In the wall case <paramref name="includeCasterTile"/> is false, so the walk
        ///     stops before the caster's tile, guaranteeing no double-occupancy.
        ///   • Returns <paramref name="from"/> if no forward tile is reachable (unit stays put).
        /// </summary>
        private Tile CalculatePullDestination(Tile from, Tile casterTile, Vector2Int pullDir, bool includeCasterTile)
        {
            Tile bestReachable = from;
            Tile current = from;

            int maxSteps = 20;
            while (maxSteps-- > 0)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, pullDir);

                if (next == null || !next.passableTerrain) break;

                // Caster's tile: honour the wall-case flag and always stop the walk here.
                if (next == casterTile)
                {
                    if (includeCasterTile) bestReachable = casterTile;
                    break;
                }

                // Any other occupied tile is an impassable blocker.
                if (next.occupied) break;

                bestReachable = next;
                current = next;
            }

            return bestReachable;
        }

        /// <summary>
        /// Animates the hooked unit to <paramref name="destination"/> in a single smooth
        /// lerp, duration scaled by tile distance.
        /// </summary>
        private IEnumerator PullToCasterTile(AbilityContext ctx, Unit hookedUnit, Tile destination)
        {
            if (hookedUnit.currentTile == destination) yield break;

            int distance = GridManager.Instance.GetGridDistance(hookedUnit.currentTile, destination);
            if (distance == 0) yield break;

            var hookedAnimator = hookedUnit.GetComponent<UnitAnimator>();

            // Face the hooked unit toward the caster (opposite of aim direction).
            hookedUnit.FaceDirection(new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y));

            hookedAnimator?.PlayKnockbackStart();
            yield return new WaitForSeconds(knockbackStartDelay);

            float totalDuration = distance * pullDurationPerTile;
            bool pullComplete = false;
            hookedUnit.AnimateToTile(destination, totalDuration, () => pullComplete = true);
            while (!pullComplete)
                yield return null;

            hookedAnimator?.PlayKnockbackEnd();
            yield return new WaitForSeconds(pullKnockbackEndDuration);
        }

        // ── Recoil helper ─────────────────────────────────────────────────────────

        /// <summary>
        /// Animates the caster sliding to the end of <paramref name="recoilPath"/> in a
        /// single smooth lerp. Called only when recoilPath.Count > 0.
        /// </summary>
        private IEnumerator ApplyRecoilWithAnimation(Unit caster, Vector2Int recoilDir, List<Tile> recoilPath)
        {
            var casterAnimator = caster.GetComponent<UnitAnimator>();

            casterAnimator?.PlayKnockbackStart();
            yield return new WaitForSeconds(knockbackStartDelay);

            Tile landingTile = recoilPath[recoilPath.Count - 1];
            float totalDuration = recoilPath.Count * recoilDurationPerTile;
            bool recoilComplete = false;
            caster.AnimateToTile(landingTile, totalDuration, () => recoilComplete = true);
            while (!recoilComplete)
                yield return null;

            casterAnimator?.PlayKnockbackEnd();
            yield return new WaitForSeconds(knockbackEndDuration);
        }

        // ── Path calculation ──────────────────────────────────────────────────────

        /// <summary>
        /// Calculates a knockback path for <paramref name="unit"/> in
        /// <paramref name="dir"/> up to <paramref name="distance"/> tiles.
        /// Stops at impassable terrain or an occupied tile.
        /// </summary>
        private List<Tile> CalculatePath(Unit unit, Vector2Int dir, int distance)
        {
            var path  = new List<Tile>();
            Tile current = unit.currentTile;
            if (current == null) return path;

            for (int i = 0; i < distance; i++)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, dir);
                if (next == null || !next.passableTerrain) break;
                if (next.currentUnit != null && next.currentUnit != unit) break;

                path.Add(next);
                current = next;
            }

            return path;
        }

        // ── Status helper ─────────────────────────────────────────────────────────

        private void ApplyShocked(Unit target, Unit source)
        {
            if (shockedStatusData == null)
            {
                Debug.LogWarning("[WiringFault] shockedStatusData is not assigned on the " +
                                 "WiringFaultEffect asset — Shocked was not applied.");
                return;
            }

            StatusEffectManager.Instance?.ApplyStatusEffect(
                target, shockedStatusData, source, shockedDuration, shockedDamage);

            Debug.Log($"[WiringFault] {target.name} is Shocked for {shockedDuration} turns.");
        }

        // ── Player input restore ──────────────────────────────────────────────────

        private IEnumerator DelayedInputRestore(CombatManager combatManager, Unit caster)
        {
            yield return null; // One frame to let animation state settle.
            combatManager.ReleaseAnimationBlock();

            // Refresh movement highlights so the player sees their remaining options.
            if (combatManager.CanMove)
            {
                GridManager.Instance.SetHighlightMode(
                    GridManager.HighlightMode.Movement, caster);
            }
        }
    }
}