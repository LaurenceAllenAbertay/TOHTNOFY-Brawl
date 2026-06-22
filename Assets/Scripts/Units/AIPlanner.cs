using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Decides what an AI unit should do this turn.
    ///
    /// Priority order:
    ///   1. Kill shot (from current tile, then from a tile we can walk or jump to)
    ///   2. Best damage opportunity (same two-pass order)
    ///   3. Useful self-buff (only when no attack is available)
    ///   4. Approach the nearest target (walk or jump, whichever gets closer)
    ///
    /// aggressionBias: 1 = always charge, 0 = pick safest tile that still advances.
    /// lookAheadSteps: 1 = score this turn only, 2 = also reward tiles in ability range next turn.
    ///
    /// Plain C# class — no MonoBehaviour.
    /// </summary>
    public class AIPlanner
    {
        private readonly Unit  unit;
        private readonly float aggressionBias;
        private readonly int   lookAheadSteps;
        private readonly System.Func<Unit, bool> canTargetUnit;
        private readonly AIDebugLogger logger;

        public AIPlanner(
            Unit unit,
            float aggressionBias,
            int lookAheadSteps,
            System.Func<Unit, bool> canTargetUnit,
            System.Func<Unit, bool> isAlly,
            AIDebugLogger logger)
        {
            this.unit           = unit;
            this.aggressionBias = aggressionBias;
            this.lookAheadSteps = lookAheadSteps;
            this.canTargetUnit  = canTargetUnit;
            this.logger         = logger;
        }

        // ── Public entry point ────────────────────────────────────────────────────

        public ActionPlan Plan(List<Unit> targets)
        {
            var abilities      = UnitLoadoutManager.GetAbilities(unit);
            var reachableTiles = GetReachableTiles();
            var jumpableTiles  = GetJumpableTiles();

            var plan = TryFindKillShot(abilities, targets, reachableTiles, jumpableTiles)
                    ?? TryFindBestDamage(abilities, targets, reachableTiles, jumpableTiles)
                    ?? TryFindUsefulBuff(abilities)
                    ?? TryTeleportToPosition(abilities, targets, reachableTiles, jumpableTiles)
                    ?? TryApproach(targets, reachableTiles, jumpableTiles, abilities);

            if (plan != null)
                logger?.LogSelectionReason(plan.debugReason);
            else
                logger?.LogNoValidActions();

            return plan;
        }

        // ── Stage 1: Kill shot ────────────────────────────────────────────────────

        private ActionPlan TryFindKillShot(Ability[] abilities, List<Unit> targets, List<Tile> reachableTiles, List<Tile> jumpableTiles)
        {
            foreach (var (ability, slot) in UsableAbilities(abilities))
            foreach (var target in targets)
            {
                if (!canTargetUnit(target)) continue;
                if (SimulateDamage(ability, target) < target.currentHealth) continue;

                // Already in range — no movement needed.
                var plan = BuildAbilityPlan(ability, slot, unit.currentTile, target, isAbilityFirst: true);
                if (plan != null) { plan.debugReason = "Kill shot (no move)"; return plan; }

                // Walk then kill.
                foreach (var tile in reachableTiles)
                {
                    if (tile == unit.currentTile) continue;
                    plan = BuildAbilityPlan(ability, slot, tile, target, isAbilityFirst: false);
                    if (plan != null) { plan.movementTarget = tile; plan.debugReason = "Kill shot (after walk)"; return plan; }
                }

                // Jump then kill.
                foreach (var tile in jumpableTiles)
                {
                    plan = BuildAbilityPlan(ability, slot, tile, target, isAbilityFirst: false);
                    if (plan != null) { plan.movementTarget = tile; plan.isJump = true; plan.debugReason = "Kill shot (after jump)"; return plan; }
                }
            }
            return null;
        }

        // ── Stage 2: Best damage ──────────────────────────────────────────────────

        private ActionPlan TryFindBestDamage(Ability[] abilities, List<Unit> targets, List<Tile> reachableTiles, List<Tile> jumpableTiles)
        {
            ActionPlan best    = null;
            int        bestDmg = 0;
            int        bestHp  = int.MaxValue;

            foreach (var (ability, slot) in UsableAbilities(abilities))
            {
                if (!DealsDamage(ability)) continue;

                foreach (var target in targets)
                {
                    if (!canTargetUnit(target)) continue;
                    int dmg = SimulateDamage(ability, target);
                    if (dmg <= 0) continue;

                    // Already in range.
                    var plan = BuildAbilityPlan(ability, slot, unit.currentTile, target, isAbilityFirst: true);
                    if (plan != null && IsBetter(dmg, target.currentHealth, bestDmg, bestHp))
                    {
                        best = plan; bestDmg = dmg; bestHp = target.currentHealth;
                        best.debugReason = "Best damage (no move)";
                    }

                    // Walk then attack.
                    foreach (var tile in reachableTiles)
                    {
                        if (tile == unit.currentTile) continue;
                        plan = BuildAbilityPlan(ability, slot, tile, target, isAbilityFirst: false);
                        if (plan != null && IsBetter(dmg, target.currentHealth, bestDmg, bestHp))
                        {
                            plan.movementTarget = tile;
                            best = plan; bestDmg = dmg; bestHp = target.currentHealth;
                            best.debugReason = "Best damage (after walk)";
                        }
                    }

                    // Jump then attack.
                    foreach (var tile in jumpableTiles)
                    {
                        plan = BuildAbilityPlan(ability, slot, tile, target, isAbilityFirst: false);
                        if (plan != null && IsBetter(dmg, target.currentHealth, bestDmg, bestHp))
                        {
                            plan.movementTarget = tile;
                            plan.isJump = true;
                            best = plan; bestDmg = dmg; bestHp = target.currentHealth;
                            best.debugReason = "Best damage (after jump)";
                        }
                    }
                }
            }

            return best;
        }

        // ── Stage 3: Useful buff ──────────────────────────────────────────────────

        private ActionPlan TryFindUsefulBuff(Ability[] abilities)
        {
            foreach (var (ability, slot) in UsableAbilities(abilities))
            {
                if (DealsDamage(ability)) continue;
                if (!AppliesBuff(ability)) continue;
                if (AlreadyHasBuff(unit, ability)) continue;

                if (ability.targeting is SelfTargeting || ability.targeting is AOETargeting
                                                       || ability.targeting is SquareAOETargeting)
                {
                    return new ActionPlan
                    {
                        abilityToUse   = ability,
                        abilitySlot    = slot,
                        isAbilityFirst = true,
                        debugReason    = "Useful buff"
                    };
                }
            }
            return null;
        }

        // ── Stage 4: Teleport to position ────────────────────────────────────────

        /// <summary>
        /// If the unit has a teleport ability and no damaging action was possible this turn,
        /// use the teleport to reposition closer to a target (or into attack range for next turn).
        /// Evaluates three cases in one pass and keeps the single best scoring destination:
        ///   A) Teleport from current tile (no walk).
        ///   B) Walk to a reachable tile first, then teleport from there.
        ///   C) Jump to a jumpable tile first, then teleport from there.
        /// Only fires when stages 1-3 all returned null, so the unit never wastes a teleport
        /// when it could be attacking.
        /// </summary>
        private ActionPlan TryTeleportToPosition(Ability[] abilities, List<Unit> targets, List<Tile> reachableTiles, List<Tile> jumpableTiles)
        {
            if (targets.Count == 0) return null;

            var anchor = GetNearest(targets);
            if (anchor?.currentTile == null) return null;

            ActionPlan best      = null;
            float      bestScore = float.MinValue;
            int        maxDist   = GridManager.Instance.AllTiles.Count;

            foreach (var (ability, slot) in UsableAbilities(abilities))
            {
                if (!IsTeleportAbility(ability)) continue;

                // Evaluate teleporting from a given origin tile and update best if improved.
                // moveTile = null means no walk/jump beforehand (case A).
                void EvaluateOrigin(Tile originTile, Tile moveTile, bool moveIsJump)
                {
                    var saved = unit.currentTile;
                    try
                    {
                        unit.currentTile = originTile;
                        var ctx = new AbilityContext { caster = unit, ability = ability };

                        List<Tile> candidates;
                        if (ability.targeting is SingleTargeting single)
                            candidates = single.GetTilesInRange(ctx);
                        else if (ability.targeting is MultiTileSelectionTargeting multi)
                            candidates = multi.GetTilesInRange(ctx);
                        else
                            return;

                        foreach (var tile in candidates)
                        {
                            if (tile == null || !tile.passableTerrain || tile.occupied) continue;
                            if (tile == originTile) continue;

                            float score = ScoreTile(tile, anchor.currentTile, abilities, targets, maxDist);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = new ActionPlan
                                {
                                    abilityToUse   = ability,
                                    abilitySlot    = slot,
                                    targetTile     = tile,
                                    movementTarget = moveTile,
                                    isJump         = moveIsJump,
                                    isAbilityFirst = false,
                                    debugReason    = moveTile == null
                                                        ? "Teleport to position"
                                                        : moveIsJump
                                                            ? "Jump then teleport to position"
                                                            : "Walk then teleport to position"
                                };
                            }
                        }
                    }
                    finally
                    {
                        unit.currentTile = saved;
                    }
                }

                // Case A: teleport from current position.
                EvaluateOrigin(unit.currentTile, moveTile: null, moveIsJump: false);

                // Case B: walk to a reachable tile first, then teleport.
                foreach (var walkTile in reachableTiles)
                {
                    if (walkTile == unit.currentTile) continue;
                    EvaluateOrigin(walkTile, moveTile: walkTile, moveIsJump: false);
                }

                // Case C: jump to a jumpable tile first, then teleport.
                foreach (var jumpTile in jumpableTiles)
                {
                    EvaluateOrigin(jumpTile, moveTile: jumpTile, moveIsJump: true);
                }
            }

            return best;
        }

        private bool IsTeleportAbility(Ability ability)
            => ability?.effects != null && ability.effects.Any(e => e is TeleportEffect);

        // ── Stage 5: Approach ─────────────────────────────────────────────────────

        private ActionPlan TryApproach(List<Unit> targets, List<Tile> reachableTiles, List<Tile> jumpableTiles, Ability[] abilities)
        {
            if (targets.Count == 0) return null;

            var anchor = GetNearest(targets);
            if (anchor?.currentTile == null) return null;

            Tile  bestTile   = null;
            bool  bestIsJump = false;
            float bestScore  = float.MinValue;
            int   maxDist    = GridManager.Instance.AllTiles.Count;

            // Score walk tiles.
            foreach (var tile in reachableTiles)
            {
                if (tile == unit.currentTile) continue;
                float score = ScoreTile(tile, anchor.currentTile, abilities, targets, maxDist);
                if (score > bestScore) { bestScore = score; bestTile = tile; bestIsJump = false; }
            }

            // Score jump tiles — only preferred when they score better than any walk tile.
            foreach (var tile in jumpableTiles)
            {
                float score = ScoreTile(tile, anchor.currentTile, abilities, targets, maxDist);
                if (score > bestScore) { bestScore = score; bestTile = tile; bestIsJump = true; }
            }

            if (bestTile == null) return null;

            return new ActionPlan
            {
                movementTarget = bestTile,
                isJump         = bestIsJump,
                debugReason    = bestIsJump ? "Approaching target (jump)" : "Approaching target"
            };
        }

        private float ScoreTile(Tile tile, Tile anchorTile, Ability[] abilities, List<Unit> targets, int maxDist)
        {
            float closeness = 1f - Mathf.Clamp01((float)GridManager.Instance.GetGridDistance(tile, anchorTile, true) / maxDist);
            float danger    = DangerScore(tile);
            float score     = (closeness * aggressionBias) - (danger * (1f - aggressionBias));

            if (lookAheadSteps >= 2 && AnyDamageAbilityReachesFromTile(tile, abilities, targets))
                score += 0.3f;

            return score;
        }

        // ── Plan builder ──────────────────────────────────────────────────────────

        /// <summary>
        /// Tries to build a valid plan for using <paramref name="ability"/> against
        /// <paramref name="target"/> as if the unit were standing on <paramref name="originTile"/>.
        ///
        /// Returns null when the ability cannot reach the target from that tile.
        ///
        /// When originTile differs from unit.currentTile we temporarily assign currentTile
        /// so all traversal methods read the correct origin. The raw field is written directly
        /// (not via SetCurrentTile) so no events fire. The finally block guarantees restoration.
        /// </summary>
        private ActionPlan BuildAbilityPlan(Ability ability, int slot, Tile originTile, Unit target, bool isAbilityFirst)
        {
            if (ability?.targeting == null || originTile == null || target?.currentTile == null)
                return null;

            var saved = unit.currentTile;
            try
            {
                unit.currentTile = originTile;
                var ctx = new AbilityContext { caster = unit, ability = ability };
                return EvaluateAbilityAgainstTarget(ability, slot, originTile, target, isAbilityFirst, ctx);
            }
            finally
            {
                unit.currentTile = saved;
            }
        }

        private ActionPlan EvaluateAbilityAgainstTarget(
            Ability ability, int slot, Tile originTile, Unit target, bool isAbilityFirst, AbilityContext ctx)
        {
            // ── SingleTargeting ───────────────────────────────────────────────────
            if (ability.targeting is SingleTargeting single)
            {
                if (!single.IsWithinRange(ctx, target.currentTile)) return null;

                // Confirm the tile is still occupied by the intended target.
                // If the tile's occupant is a different unit (e.g. a body that landed
                // there after the target moved), the plan would fire at the wrong unit.
                var occupant = target.currentTile.currentUnit;
                if (occupant == null || occupant != target) return null;

                // Bodies and other neutral objects must never be planned against here.
                // SelectTargets enforces canTargetNeutral at execution time, but the AI
                // planner must reject them at planning time too so it doesn't waste a turn
                // firing an ability that SelectTargets will refuse to resolve.
                if (occupant.IsNeutral || occupant.IsDead) return null;

                bool occupantIsAlly = occupant is EnemyUnit == unit is EnemyUnit;
                if (!( (!occupantIsAlly && ability.canHitEnemies) || (occupantIsAlly && ability.canHitAllies) ))
                    return null;

                return new ActionPlan
                {
                    abilityToUse   = ability,
                    abilitySlot    = slot,
                    targetTile     = target.currentTile,
                    isAbilityFirst = isAbilityFirst
                };
            }

            // ── LineTargeting & MovementLineTargeting ─────────────────────────────
            if (ability.targeting is LineTargeting || ability.targeting is MovementLineTargeting)
            {
                var dir = DirectionToward(originTile, target.currentTile);
                if (dir == Vector2Int.zero)
                    return null;

                if (ability.targeting is LineTargeting lt && !lt.IsValidDirection(dir))
                    return null;

                // Use SelectTargets (the same path execution takes) so conditions like
                // requireEmptyDestination are respected during planning exactly as at runtime.
                // A raw traversal.Contains check misses those blocking conditions and produces
                // plans that silently fail when the ability fires.
                var selectCtx = new AbilityContext { caster = unit, ability = ability, aimDir = dir };
                var selected  = ability.targeting.SelectTargets(selectCtx);

                if (!selected.Contains(target))
                    return null;

                return new ActionPlan
                {
                    abilityToUse   = ability,
                    abilitySlot    = slot,
                    aimDirection   = dir,
                    isAbilityFirst = isAbilityFirst
                };
            }

            // ── AOE types ─────────────────────────────────────────────────────────
            if (ability.targeting is AOETargeting || ability.targeting is SquareAOETargeting
                                                  || ability.targeting is RandomAOETargeting)
            {
                // Use SelectTargets so any targeting-level restrictions are applied
                // consistently between planning and execution.
                var selected = ability.targeting.SelectTargets(ctx);
                if (!selected.Contains(target)) return null;

                return new ActionPlan
                {
                    abilityToUse   = ability,
                    abilitySlot    = slot,
                    isAbilityFirst = isAbilityFirst
                };
            }

            // ── SelfTargeting ─────────────────────────────────────────────────────
            if (ability.targeting is SelfTargeting)
            {
                return new ActionPlan
                {
                    abilityToUse   = ability,
                    abilitySlot    = slot,
                    isAbilityFirst = isAbilityFirst
                };
            }

            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private bool IsBetter(int dmg, int hp, int bestDmg, int bestHp)
            => dmg > bestDmg || (dmg == bestDmg && hp < bestHp);

        private int SimulateDamage(Ability ability, Unit target)
        {
            if (!DealsDamage(ability)) return 0;
            int baseDamage = ability.damage + unit.currentAttack;
            return Mathf.Max(1, baseDamage - target.currentDefense);
        }

        private bool DealsDamage(Ability ability)
            => ability?.effects != null && ability.effects.Any(e => e is DamageEffect);

        private bool AppliesBuff(Ability ability)
            => ability?.effects != null && ability.effects.Any(e => e is StatusEffect se && se.IsBuffEffect);

        private bool AlreadyHasBuff(Unit target, Ability ability)
        {
            if (StatusEffectManager.Instance == null) return false;
            foreach (var effect in ability.effects)
                if (effect is StatusEffect se)
                    foreach (var app in se.statusesToApply)
                    {
                        if (app.statusEffectData == null || !app.statusEffectData.effectType.IsBuffType()) continue;
                        if (!StatusEffectManager.Instance.HasStatusEffect(target, app.statusEffectData.effectType))
                            return false;
                    }
            return true;
        }

        private float DangerScore(Tile tile)
        {
            int threats = 0;
            foreach (var p in UnitManager.PlayerUnits)
            {
                if (p == null || p.currentTile == null || p.IsDead) continue;
                int dist = GridManager.Instance.GetGridDistance(p.currentTile, tile, true);
                if (dist <= p.GetEffectiveMovementRange() + MaxAbilityRange(p)) threats++;
            }
            return Mathf.Clamp01(threats / 4f);
        }

        private bool AnyDamageAbilityReachesFromTile(Tile tile, Ability[] abilities, List<Unit> targets)
        {
            foreach (var (ability, _) in UsableAbilities(abilities))
            {
                if (!DealsDamage(ability)) continue;
                foreach (var target in targets)
                {
                    if (BuildAbilityPlan(ability, -1, tile, target, false) != null)
                        return true;
                }
            }
            return false;
        }

        private List<Tile> GetReachableTiles()
        {
            if (unit.currentTile == null) return new List<Tile>();
            return GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());
        }

        /// <summary>
        /// Returns all tiles this unit can reach via a jump this turn.
        /// Uses GetGridDistance with includeYLevel=true so cross-layer tiles are included —
        /// this is the key difference from GetReachableTiles which is a same-layer BFS.
        /// Mirrors JumpSystem.GetJumpableTiles exactly.
        /// </summary>
        private List<Tile> GetJumpableTiles()
        {
            var result = new List<Tile>();
            if (unit.currentTile == null || unit.JumpRange < 2) return result;

            Tile startTile    = unit.currentTile;
            int  maxRange     = unit.JumpRange;
            const int minRange = 2;

            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || tile == startTile) continue;
                if (!tile.passableTerrain) continue;
                if (tile.occupied && !unit.CanStompOccupiedTiles) continue;

                // includeYLevel=true is what makes cross-layer tiles visible here.
                int dist = GridManager.Instance.GetGridDistance(startTile, tile, true);
                if (dist < minRange || dist > maxRange) continue;

                // Skip tiles the unit can already walk to — no point flagging a jump
                // for a tile that's already in the walk set.
                var walkPath = GridManager.Instance.FindPath(startTile, tile, unit.GetEffectiveMovementRange());
                if (walkPath.Count > 0) continue;

                // Wall check — same as JumpSystem.
                Vector3 from = startTile.transform.position + Vector3.up * 0.5f;
                Vector3 to   = tile.transform.position      + Vector3.up * 0.5f;
                if (Physics.Raycast(from, (to - from).normalized, Vector3.Distance(from, to) * 0.9f, LayerMask.GetMask("Walls")))
                    continue;

                result.Add(tile);
            }

            return result;
        }

        private Unit GetNearest(List<Unit> targets)
        {
            Unit nearest = null; int nearestDist = int.MaxValue;
            foreach (var t in targets)
            {
                if (t?.currentTile == null) continue;
                int d = GridManager.Instance.GetGridDistance(unit.currentTile, t.currentTile, true);
                if (d < nearestDist) { nearestDist = d; nearest = t; }
            }
            return nearest;
        }

        private Vector2Int DirectionToward(Tile from, Tile to)
        {
            if (from == null || to == null) return Vector2Int.zero;
            Vector3 delta = to.transform.position - from.transform.position;
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.z))
                return delta.x >= 0 ? Vector2Int.right : Vector2Int.left;
            return delta.z >= 0 ? Vector2Int.up : Vector2Int.down;
        }

        private int MaxAbilityRange(Unit u)
        {
            int max = 1;
            foreach (var a in UnitLoadoutManager.GetAbilities(u))
                if (a != null && a.range > max) max = a.range;
            return max;
        }

        private IEnumerable<(Ability, int)> UsableAbilities(Ability[] abilities)
        {
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] != null) yield return (abilities[i], i);
        }
    }
}