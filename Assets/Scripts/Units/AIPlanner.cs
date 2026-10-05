using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class AIPlanner
    {
        private readonly Unit  unit;
        private readonly float aggressionBias;
        private readonly int   lookAheadSteps;
        private readonly int   targetCandidateCount;
        private readonly System.Func<Unit, bool>  canTargetUnit;
        private readonly System.Func<List<Unit>>  getTeammates;
        private readonly System.Func<Unit>         getLastAttacker;
        private readonly JumpSystem    jumpSystem;
        private readonly AIDebugLogger logger;

        private readonly struct MoveOption
        {
            public readonly Tile destination;
            public readonly Tile jumpFrom;
            public readonly bool isJump;

            private MoveOption(Tile destination, Tile jumpFrom, bool isJump)
            {
                this.destination = destination;
                this.jumpFrom    = jumpFrom;
                this.isJump      = isJump;
            }

            public static MoveOption Walk(Tile destination)
                => new MoveOption(destination, null, false);

            public static MoveOption Jump(Tile destination)
                => new MoveOption(destination, null, true);

            public static MoveOption WalkThenJump(Tile destination, Tile jumpFrom)
                => new MoveOption(destination, jumpFrom, true);
        }

        public AIPlanner(
            Unit unit,
            float aggressionBias,
            int lookAheadSteps,
            int targetCandidateCount,
            System.Func<Unit, bool> canTargetUnit,
            System.Func<Unit, bool> isAlly,
            System.Func<List<Unit>> getTeammates,
            System.Func<Unit> getLastAttacker,
            JumpSystem jumpSystem,
            AIDebugLogger logger)
        {
            this.unit                 = unit;
            this.aggressionBias       = aggressionBias;
            this.lookAheadSteps       = lookAheadSteps;
            this.targetCandidateCount = targetCandidateCount;
            this.canTargetUnit        = canTargetUnit;
            this.getTeammates         = getTeammates;
            this.getLastAttacker      = getLastAttacker;
            this.jumpSystem           = jumpSystem;
            this.logger               = logger;
        }

        public ActionPlan Plan(List<Unit> targets)
        {
            var abilities   = UnitLoadoutManager.GetAbilities(unit);
            var moveOptions = BuildMoveOptions();

            var anchor = PickWeightedTarget(targets);

            var plan = TryFindKillShot(abilities, targets, moveOptions)
                    ?? TryAttackAnchor(abilities, anchor, moveOptions)
                    ?? TryFindUsefulBuff(abilities)
                    ?? TryTeleportToPosition(abilities, anchor, moveOptions)
                    ?? TryApproach(anchor, moveOptions, abilities);

            if (plan != null)
                logger?.LogSelectionReason(plan.debugReason);
            else
                logger?.LogNoValidActions();

            return plan;
        }

        private ActionPlan TryFindKillShot(Ability[] abilities, List<Unit> targets, List<MoveOption> moveOptions)
        {
            if (unit.currentTile != null)
            {
                foreach (var t in targets)
                {
                    if (t.currentTile == null) continue;
                    if (!canTargetUnit(t)) continue;
                    if (GridManager.Instance.GetGridDistance(unit.currentTile, t.currentTile, true) == 1)
                        return null;
                }
            }

            var ordered = targets.OrderBy(t => HealthPct(t)).ToList();

            foreach (var (ability, slot) in UsableAbilities(abilities))
            foreach (var target in ordered)
            {
                if (!canTargetUnit(target)) continue;
                if (SimulateDamage(ability, target) < target.currentHealth) continue;

                var plan = BuildAbilityPlan(ability, slot, unit.currentTile, target, isAbilityFirst: true);
                if (plan != null) { plan.debugReason = "Kill shot (no move)"; return plan; }

                foreach (var move in moveOptions)
                {
                    plan = BuildAbilityPlan(ability, slot, move.destination, target, isAbilityFirst: false);
                    if (plan == null) continue;

                    ApplyMove(plan, move);
                    plan.debugReason = $"Kill shot (after {MoveLabel(move)})";
                    return plan;
                }
            }
            return null;
        }

        private ActionPlan TryAttackAnchor(Ability[] abilities, Unit anchor, List<MoveOption> moveOptions)
        {
            if (anchor == null) return null;

            ActionPlan best     = null;
            int        bestDmg  = 0;
            int        bestRank = int.MaxValue;

            foreach (var (ability, slot) in UsableAbilities(abilities))
            {
                if (!DealsDamage(ability)) continue;
                int dmg = SimulateDamage(ability, anchor);
                if (dmg <= 0) continue;

                if (IsBetterAttack(dmg, StayRank, bestDmg, bestRank))
                {
                    var stayPlan = BuildAbilityPlan(ability, slot, unit.currentTile, anchor, isAbilityFirst: true);
                    if (stayPlan != null)
                    {
                        stayPlan.debugReason = "Attack anchor (no move)";
                        best = stayPlan; bestDmg = dmg; bestRank = StayRank;
                    }
                }

                foreach (var move in moveOptions)
                {
                    int rank = MoveRank(move);
                    if (!IsBetterAttack(dmg, rank, bestDmg, bestRank)) continue;

                    var plan = BuildAbilityPlan(ability, slot, move.destination, anchor, isAbilityFirst: false);
                    if (plan == null) continue;

                    ApplyMove(plan, move);
                    plan.debugReason = $"Attack anchor (after {MoveLabel(move)})";
                    best = plan; bestDmg = dmg; bestRank = rank;
                }
            }

            return best;
        }

        private static bool IsBetterAttack(int damage, int moveRank, int bestDamage, int bestRank)
            => damage > bestDamage || (damage == bestDamage && moveRank < bestRank);

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

        private ActionPlan TryTeleportToPosition(Ability[] abilities, Unit anchor, List<MoveOption> moveOptions)
        {
            if (anchor?.currentTile == null) return null;

            ActionPlan best      = null;
            float      bestScore = float.MinValue;
            int        maxDist   = GridManager.Instance.AllTiles.Count;

            var moveDestinations = new HashSet<Tile>();
            foreach (var move in moveOptions)
                moveDestinations.Add(move.destination);

            foreach (var (ability, slot) in UsableAbilities(abilities))
            {
                if (!IsTeleportAbility(ability)) continue;

                void EvaluateOrigin(Tile originTile, MoveOption? move)
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

                            if (moveDestinations.Contains(tile)) continue;

                            float score = ScoreTile(tile, anchor.currentTile, abilities, anchor, maxDist);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                best = new ActionPlan
                                {
                                    abilityToUse   = ability,
                                    abilitySlot    = slot,
                                    targetTile     = tile,
                                    isAbilityFirst = false,
                                    debugReason    = !move.HasValue
                                                        ? "Teleport to position"
                                                        : $"{MoveLabel(move.Value)} then teleport to position"
                                };

                                if (move.HasValue)
                                    ApplyMove(best, move.Value);
                            }
                        }
                    }
                    finally
                    {
                        unit.currentTile = saved;
                    }
                }

                EvaluateOrigin(unit.currentTile, null);

                foreach (var move in moveOptions)
                    EvaluateOrigin(move.destination, move);
            }

            return best;
        }

        private bool IsTeleportAbility(Ability ability)
            => ability?.effects != null && ability.effects.Any(e => e is TeleportEffect);

        private ActionPlan TryApproach(Unit anchor, List<MoveOption> moveOptions, Ability[] abilities)
        {
            if (anchor?.currentTile == null) return null;

            MoveOption? bestMove  = null;
            float       bestScore = float.MinValue;
            int         maxDist   = GridManager.Instance.AllTiles.Count;

            foreach (var move in moveOptions)
            {
                float score = ScoreTile(move.destination, anchor.currentTile, abilities, anchor, maxDist);
                if (score > bestScore) { bestScore = score; bestMove = move; }
            }

            if (!bestMove.HasValue) return null;

            var plan = new ActionPlan
            {
                debugReason = $"Approaching anchor ({MoveLabel(bestMove.Value)})"
            };
            ApplyMove(plan, bestMove.Value);
            return plan;
        }

        private float ScoreTile(Tile tile, Tile anchorTile, Ability[] abilities, Unit anchor, int maxDist)
        {
            const int yLevelWeight = 3;

            int xzDistance = GridManager.Instance.GetGridDistance(tile, anchorTile, includeYLevel: false);
            int yDistance  = Mathf.Abs(GridManager.Instance.GetYLevel(tile) - GridManager.Instance.GetYLevel(anchorTile));
            int weightedDistance = xzDistance + (yDistance * yLevelWeight);

            float closeness = 1f - Mathf.Clamp01((float)weightedDistance / maxDist);
            float danger    = DangerScore(tile);
            float score     = (closeness * aggressionBias) - (danger * (1f - aggressionBias));

            if (GridManager.Instance.IsSameYLevel(tile, anchorTile))
                score += 0.2f;

            if (lookAheadSteps >= 2 && AnyDamageAbilityReachesAnchorFromTile(tile, abilities, anchor))
                score += 0.3f;

            return score;
        }

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
            if (ability.targeting is SingleTargeting single)
            {
                if (!single.IsWithinRange(ctx, target.currentTile)) return null;

                var occupant = target.currentTile.currentUnit;
                if (occupant == null || occupant != target) return null;

                if (occupant.IsNeutral || occupant.IsDead) return null;

                bool occupantIsAlly = occupant.IsAllyOf(unit);
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

            if (ability.targeting is LineTargeting || ability.targeting is MovementLineTargeting)
            {
                var dir = DirectionToward(originTile, target.currentTile);
                if (dir == Vector2Int.zero)
                    return null;

                if (ability.targeting is LineTargeting lt && !lt.IsValidDirection(dir))
                    return null;

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

            if (ability.targeting is AOETargeting || ability.targeting is SquareAOETargeting
                                                  || ability.targeting is RandomAOETargeting)
            {
                var selected = ability.targeting.SelectTargets(ctx);
                if (!selected.Contains(target)) return null;

                return new ActionPlan
                {
                    abilityToUse   = ability,
                    abilitySlot    = slot,
                    isAbilityFirst = isAbilityFirst
                };
            }

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

        private Unit PickWeightedTarget(List<Unit> targets)
        {
            if (targets.Count == 0) return null;
            if (targets.Count == 1) return targets[0];
            if (unit.currentTile == null) return targets[0];

            const int meleeRange = 2;

            var validTargets = targets
                .Where(t => t.currentTile != null)
                .ToList();

            if (validTargets.Count == 0) return targets[0];

            var meleeTargets = validTargets
                .Where(t => GridManager.Instance.GetGridDistance(unit.currentTile, t.currentTile, true) <= meleeRange)
                .ToList();

            if (meleeTargets.Count == 1)
                return meleeTargets[0];

            if (meleeTargets.Count > 1)
            {
                var lastAttacker = getLastAttacker?.Invoke();
                if (lastAttacker != null && meleeTargets.Contains(lastAttacker))
                    return lastAttacker;

                var meleeWeights = BuildProximityWeights(meleeTargets);
                return WeightedRandom(meleeTargets, meleeWeights);
            }

            var candidates = validTargets
                .OrderBy(t => GridManager.Instance.GetGridDistance(unit.currentTile, t.currentTile, true))
                .Take(targetCandidateCount)
                .ToList();

            if (candidates.Count == 0) return targets[0];
            if (candidates.Count == 1) return candidates[0];

            var weights = BuildProximityWeights(candidates);
            return WeightedRandom(candidates, weights);
        }

        private float[] BuildProximityWeights(List<Unit> candidates)
        {
            var teammates = getTeammates?.Invoke() ?? new List<Unit>();
            var weights   = new float[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                int dist = GridManager.Instance.GetGridDistance(unit.currentTile, candidates[i].currentTile, true);
                float proximityWeight  = 1f / Mathf.Max(1, dist);
                float pressurePenalty  = Mathf.Pow(2f, CountTeammatePressure(candidates[i], candidates, teammates));
                weights[i] = proximityWeight / pressurePenalty;
            }
            return weights;
        }

        private int CountTeammatePressure(Unit candidate, List<Unit> candidates, List<Unit> teammates)
        {
            int count = 0;
            foreach (var teammate in teammates)
            {
                if (teammate?.currentTile == null) continue;

                Unit nearestToTeammate = null;
                int  nearestDist       = int.MaxValue;
                foreach (var c in candidates)
                {
                    if (c?.currentTile == null) continue;
                    int d = GridManager.Instance.GetGridDistance(teammate.currentTile, c.currentTile, true);
                    if (d < nearestDist) { nearestDist = d; nearestToTeammate = c; }
                }

                if (nearestToTeammate == candidate) count++;
            }
            return count;
        }

        private static Unit WeightedRandom(List<Unit> candidates, float[] weights)
        {
            float total = 0f;
            foreach (var w in weights) total += w;
            if (total <= 0f) return candidates[0];

            float roll       = Random.Range(0f, total);
            float cumulative = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative) return candidates[i];
            }
            return candidates[candidates.Count - 1]; 
        }

        private float HealthPct(Unit u)
            => u != null && u.maxHealth > 0
                ? u.currentHealth / (float)u.maxHealth
                : 1f;

        private int SimulateDamage(Ability ability, Unit target)
        {
            if (!DealsDamage(ability)) return 0;

            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(target, StatusEffectType.Invulnerable))
                return 0;

            float attackMultiplier = StatusEffectManager.GetAttackMultiplier(unit);
            int baseDamage = Mathf.RoundToInt((ability.damage + unit.baseAttack) * attackMultiplier);
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

        private bool AnyDamageAbilityReachesAnchorFromTile(Tile tile, Ability[] abilities, Unit anchor)
        {
            if (anchor == null) return false;
            foreach (var (ability, _) in UsableAbilities(abilities))
            {
                if (!DealsDamage(ability)) continue;
                if (BuildAbilityPlan(ability, -1, tile, anchor, false) != null)
                    return true;
            }
            return false;
        }

        private const int StayRank         = 0;
        private const int WalkRank         = 1;
        private const int JumpRank         = 2;
        private const int WalkThenJumpRank = 3;

        private static int MoveRank(MoveOption move)
            => !move.isJump ? WalkRank : move.jumpFrom == null ? JumpRank : WalkThenJumpRank;

        private static string MoveLabel(MoveOption move)
            => !move.isJump ? "walk" : move.jumpFrom == null ? "jump" : "walk then jump";

        private static void ApplyMove(ActionPlan plan, MoveOption move)
        {
            plan.movementTarget = move.destination;
            plan.isJump         = move.isJump;
            plan.jumpFromTile   = move.jumpFrom;
        }

        private List<MoveOption> BuildMoveOptions()
        {
            var options = new List<MoveOption>();
            if (unit.currentTile == null || !unit.CanMove()) return options;

            int  movementRange = unit.GetEffectiveMovementRange();
            var  walkTiles     = GridManager.Instance.GetReachableTiles(unit.currentTile, movementRange);
            var  walkReachable = new HashSet<Tile>(walkTiles);

            foreach (var tile in walkTiles)
            {
                if (tile == unit.currentTile) continue;
                options.Add(MoveOption.Walk(tile));
            }

            if (jumpSystem == null || !jumpSystem.CanUnitJump(unit)) return options;
            if (movementRange < JumpSystem.MinJumpRange) return options;

            var claimedDestinations = new HashSet<Tile>();

            foreach (var tile in JumpDestinationsFrom(unit.currentTile, walkReachable, claimedDestinations))
                options.Add(MoveOption.Jump(tile));

            foreach (var launchTile in LaunchTilesWithMovementToSpare(walkTiles, movementRange))
                foreach (var tile in JumpDestinationsFrom(launchTile, walkReachable, claimedDestinations))
                    options.Add(MoveOption.WalkThenJump(tile, launchTile));

            return options;
        }

        private List<Tile> LaunchTilesWithMovementToSpare(List<Tile> walkTiles, int movementRange)
        {
            var affordable = new List<(Tile tile, int cost)>();

            foreach (var tile in walkTiles)
            {
                if (tile == unit.currentTile) continue;

                int cost = GridManager.Instance.FindPath(unit.currentTile, tile, movementRange).Count;
                if (cost <= 0) continue;
                if (movementRange - cost < JumpSystem.MinJumpRange) continue;

                affordable.Add((tile, cost));
            }

            affordable.Sort((a, b) => a.cost.CompareTo(b.cost));
            return affordable.Select(entry => entry.tile).ToList();
        }

        private List<Tile> JumpDestinationsFrom(Tile origin, HashSet<Tile> walkReachable, HashSet<Tile> claimedDestinations)
        {
            var destinations = new List<Tile>();

            foreach (var tile in jumpSystem.GetJumpableTilesFrom(unit, origin))
            {
                if (tile == unit.currentTile) continue;
                if (walkReachable.Contains(tile)) continue;
                if (!claimedDestinations.Add(tile)) continue;

                destinations.Add(tile);
            }

            return destinations;
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
            if (!unit.CanUseAbilities()) yield break;
            for (int i = 0; i < abilities.Length; i++)
                if (abilities[i] != null && !unit.IsAbilityOnCooldown(i))
                    yield return (abilities[i], i);
        }
    }
}