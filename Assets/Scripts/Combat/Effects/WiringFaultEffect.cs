using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Wiring Fault — Kallper's signature ability.
    ///
    /// Sequence (all steps are animated and sequential within one coroutine):
    ///   1. Pull  — the first unit in the line is dragged to the caster's tile.
    ///              The hooked unit takes full ability damage on arrival.
    ///   2. Recoil — the caster is knocked back 2 tiles (opposite aim direction).
    ///   3. Branch:
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

        [Tooltip("Brief pause after the hooked unit arrives before the recoil begins.")]
        public float pauseAfterPull = 0.2f;

        [Tooltip("Time in seconds for the caster to travel one tile during recoil.")]
        public float recoilDurationPerTile = 0.18f;

        [Tooltip("Duration of Knockback_End animation played on caster after recoil.")]
        public float knockbackEndDuration = 0.4f;

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

            yield return ctx.caster.StartCoroutine(PullToCasterTile(ctx, hookedUnit, casterTile));

            // Deal full damage to the hooked unit on arrival.
            if (!hookedUnit.IsDead)
            {
                int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);
                int finalDamage = Mathf.Max(1, baseDamage - hookedUnit.currentDefense);
                hookedUnit.ReceiveDamage(finalDamage, ctx.caster);
                UnitManager.NotifyUnitDamaged(hookedUnit, ctx.caster);
                ctx.LastResolvedDamage += finalDamage;
                Debug.Log($"[WiringFault] {hookedUnit.name} took {finalDamage} damage on arrival.");
            }

            // Brief dramatic pause before the recoil.
            yield return new WaitForSeconds(pauseAfterPull);

            // ── PHASE 2: Caster recoil knockback ─────────────────────────────────
            // Knock the caster back in the direction opposite to the aim direction.
            Vector2Int recoilDir = new Vector2Int(-ctx.aimDir.x, -ctx.aimDir.y);

            // Build the recoil path — stop at walls, impassable tiles, or occupied tiles.
            List<Tile> recoilPath = CalculatePath(ctx.caster, recoilDir, recoilDistance);

            // Block player input for the duration of the recoil animation.
            bool isPlayerCaster = ctx.caster is PlayerUnit;
            CombatManager combatManager = null;
            if (isPlayerCaster)
            {
                combatManager = Object.FindAnyObjectByType<CombatManager>();
                if (combatManager != null && combatManager.CurrentActiveUnit == ctx.caster)
                    combatManager.BlockAnimationForEffect();
            }

            yield return ctx.caster.StartCoroutine(
                ApplyRecoilWithAnimation(ctx, ctx.caster, recoilDir, recoilPath));

            // ── PHASE 3: Branch — hit or miss ────────────────────────────────────
            bool collisionHit = false;

            if (recoilPath.Count > 0)
            {
                // Check the tile immediately beyond the caster's landing tile.
                Tile landingTile = recoilPath[recoilPath.Count - 1];
                Tile beyondTile  = GridManager.Instance.GetTileInDirection(landingTile, recoilDir);

                if (beyondTile != null &&
                    beyondTile.currentUnit != null &&
                    beyondTile.currentUnit != ctx.caster &&
                    !beyondTile.currentUnit.IsDead)
                {
                    collisionHit = true;
                    Unit victim = beyondTile.currentUnit;

                    // Double damage to the unit the caster slams into.
                    int baseDamage = ctx.ability.damage + (ctx.caster != null ? ctx.caster.currentAttack : 0);
                    int doubleDamage = Mathf.Max(1, (baseDamage - victim.currentDefense) * 2);
                    victim.ReceiveDamage(doubleDamage, ctx.caster);
                    UnitManager.NotifyUnitDamaged(victim, ctx.caster);
                    ctx.LastResolvedDamage += doubleDamage;
                    Debug.Log($"[WiringFault] Recoil slammed into {victim.name} — {doubleDamage} damage (double).");

                    // Play hurt animation on the victim.
                    victim.GetComponent<UnitAnimator>()?.PlayHurt();

                    // Apply Shocked to the collision victim.
                    ApplyShocked(victim, ctx.caster);
                }
            }

            if (!collisionHit)
            {
                // Miss branch — caster gets Shocked.
                Debug.Log("[WiringFault] Recoil missed — caster takes Shocked.");
                ApplyShocked(ctx.caster, ctx.caster);

                // Play debuff animation on the caster.
                var casterAnimator = ctx.caster.GetComponent<UnitAnimator>();
                if (casterAnimator != null && casterAnimator.HasState("Debuff"))
                    casterAnimator.PlayAnimation("Debuff");
                else
                    casterAnimator?.PlayHurt();

                yield return new WaitForSeconds(0.5f);
            }

            // Restore player input now that the full sequence is done.
            if (isPlayerCaster && combatManager != null &&
                combatManager.CurrentActiveUnit == ctx.caster)
            {
                yield return ctx.caster.StartCoroutine(
                    DelayedInputRestore(combatManager, ctx.caster));
            }
        }

        // ── Pull helper ───────────────────────────────────────────────────────────

        /// <summary>
        /// Animates the hooked unit sliding tile-by-tile toward <paramref name="destination"/>.
        /// The unit plays the Knockback_Start animation for the duration of the journey.
        /// </summary>
        private IEnumerator PullToCasterTile(AbilityContext ctx, Unit hookedUnit, Tile destination)
        {
            if (hookedUnit.currentTile == destination) yield break;

            var hookedAnimator = hookedUnit.GetComponent<UnitAnimator>();

            // Build a path from the hooked unit's tile to the caster's tile, stepping
            // back toward the caster one tile at a time.
            List<Tile> pullPath = BuildPullPath(hookedUnit.currentTile, destination);
            if (pullPath.Count == 0) yield break;

            // Face the hooked unit toward the caster (pull direction).
            Vector2Int pullDir = ctx.aimDir; // hooked unit moves opposite to aimDir (back toward caster)
            hookedUnit.FaceDirection(new Vector2Int(-pullDir.x, -pullDir.y));

            hookedAnimator?.PlayKnockbackStart();
            yield return new WaitForSeconds(0.1f);

            foreach (var tile in pullPath)
            {
                bool moveComplete = false;
                hookedUnit.AnimateToTile(tile, pullDurationPerTile, () => moveComplete = true);
                while (!moveComplete)
                    yield return null;
            }

            hookedAnimator?.PlayKnockbackEnd();
            yield return new WaitForSeconds(0.25f);
        }

        /// <summary>
        /// Returns the sequence of tiles from <paramref name="from"/> toward
        /// <paramref name="to"/>, stepping one tile at a time in the cardinal direction
        /// that closes the gap. Stops if a tile is impassable or occupied (by a unit
        /// other than the caster, who is expected to be on <paramref name="to"/>).
        /// </summary>
        private List<Tile> BuildPullPath(Tile from, Tile to)
        {
            var path = new List<Tile>();
            if (from == null || to == null) return path;

            // Determine the step direction (horizontal only — matches LineTargeting).
            Vector3 diff = to.transform.position - from.transform.position;
            int dx = diff.x > 0.01f ? 1 : (diff.x < -0.01f ? -1 : 0);
            int dz = diff.z > 0.01f ? 1 : (diff.z < -0.01f ? -1 : 0);
            // Prefer the dominant axis (should always be a clean cardinal for a line ability).
            Vector2Int step = Mathf.Abs(diff.x) >= Mathf.Abs(diff.z)
                ? new Vector2Int(dx, 0)
                : new Vector2Int(0, dz);

            Tile current = from;
            // Safety cap to prevent infinite loops on malformed grids.
            int maxSteps = 20;
            while (current != to && maxSteps-- > 0)
            {
                Tile next = GridManager.Instance.GetTileInDirection(current, step);
                if (next == null) break;

                // Stop if the path is physically blocked (except the destination itself —
                // the caster is standing there and will share it momentarily).
                if (next != to && (!next.passableTerrain || (next.occupied && next.currentUnit != null)))
                    break;

                path.Add(next);
                current = next;
            }

            return path;
        }

        // ── Recoil helper ─────────────────────────────────────────────────────────

        /// <summary>
        /// Animates the caster sliding along <paramref name="recoilPath"/> using the
        /// standard Knockback_Start → move → Knockback_End sequence.
        /// </summary>
        private IEnumerator ApplyRecoilWithAnimation(
            AbilityContext ctx, Unit caster, Vector2Int recoilDir, List<Tile> recoilPath)
        {
            var casterAnimator = caster.GetComponent<UnitAnimator>();

            // Face the caster in the direction of recoil so the sprite reads correctly.
            caster.FaceDirection(recoilDir);

            if (recoilPath.Count == 0)
            {
                // Nowhere to go — still play the animation for visual feedback.
                casterAnimator?.PlayKnockbackStart();
                yield return new WaitForSeconds(0.3f);
                casterAnimator?.PlayKnockbackEnd();
                yield return new WaitForSeconds(knockbackEndDuration);
                yield break;
            }

            casterAnimator?.PlayKnockbackStart();
            yield return new WaitForSeconds(0.1f);

            foreach (var tile in recoilPath)
            {
                bool moveComplete = false;
                caster.AnimateToTile(tile, recoilDurationPerTile, () => moveComplete = true);
                while (!moveComplete)
                    yield return null;
            }

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