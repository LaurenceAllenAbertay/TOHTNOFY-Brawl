using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Pure action evaluation and selection logic for one AI unit.
    /// Plain class — no MonoBehaviour, no coroutines, no FindAnyObjectByType.
    /// Instantiated by UnitAI and called synchronously during the thinking phase.
    /// </summary>
    public class AIEvaluator
    {
        #region Fields

        private readonly Unit unit;
        private readonly int difficulty;
        private readonly UnitAI.AIPersonality personality;
        private readonly System.Func<Unit, bool> canTargetUnit;
        private readonly System.Func<Unit, bool> isAlly;
        private readonly AIDebugLogger logger;

        #endregion

        #region Constructor

        public AIEvaluator(
            Unit unit,
            int difficulty,
            UnitAI.AIPersonality personality,
            System.Func<Unit, bool> canTargetUnit,
            System.Func<Unit, bool> isAlly,
            AIDebugLogger logger)
        {
            this.unit = unit;
            this.difficulty = difficulty;
            this.personality = personality;
            this.canTargetUnit = canTargetUnit;
            this.isAlly = isAlly;
            this.logger = logger;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Generates and scores all possible action plans for this turn.
        /// Returns plans sorted by score descending.
        /// </summary>
        public List<ActionPlan> EvaluateAllActions(List<Unit> targets, List<UnitAI> teammates)
        {
            var evaluatedActions = new List<ActionPlan>();

            logger.LogTargetsAndTeammates(targets, teammates);

            EvaluateAbilityFirstStrategies(evaluatedActions, targets, teammates);
            EvaluateAbilityOptions(evaluatedActions, targets, teammates);
            EvaluateJumpOptions(evaluatedActions, targets, teammates);
            EvaluateMovementOptions(evaluatedActions, targets, teammates);

            evaluatedActions = evaluatedActions.OrderByDescending(a => a.totalScore).ToList();
            return evaluatedActions;
        }

        /// <summary>
        /// Selects the best action plan from a sorted list, introducing randomness scaled to difficulty.
        /// </summary>
        public ActionPlan SelectBestAction(List<ActionPlan> evaluatedActions)
        {
            if (evaluatedActions.Count == 0) return null;

            if (difficulty >= 8)
            {
                logger.LogSelectionReason("High difficulty - selecting optimal action");
                return evaluatedActions[0];
            }
            else if (difficulty >= 5)
            {
                int topCount = Mathf.Min(3, evaluatedActions.Count);
                float[] weights = { 0.6f, 0.3f, 0.1f };
                float random = Random.Range(0f, 1f);
                float cumulative = 0f;

                for (int i = 0; i < topCount; i++)
                {
                    cumulative += weights[i];
                    if (random <= cumulative)
                    {
                        logger.LogSelectionReason($"Medium difficulty - selected option #{i + 1}");
                        return evaluatedActions[i];
                    }
                }
                return evaluatedActions[0];
            }
            else
            {
                int topHalf = Mathf.Max(1, evaluatedActions.Count / 2);
                int selectedIndex = Random.Range(0, topHalf);
                logger.LogSelectionReason($"Low difficulty - randomly selected option #{selectedIndex + 1} from top {topHalf}");
                return evaluatedActions[selectedIndex];
            }
        }

        #endregion

        #region Strategy Evaluators

        private void EvaluateAbilityFirstStrategies(List<ActionPlan> actions, List<Unit> targets, List<UnitAI> teammates)
        {
            if (unit.characterData?.abilityLoadout == null) return;

            for (int i = 0; i < unit.characterData.abilityLoadout.Length; i++)
            {
                var ability = unit.characterData.abilityLoadout[i];
                if (ability == null) continue;

                int optionsForThisAbility = EvaluateAbilityFromPosition(actions, ability, i, unit.currentTile, targets, teammates);

                if (optionsForThisAbility > 0)
                    EvaluatePostAbilityMovement(actions, ability, i, targets, teammates);
            }
        }

        private void EvaluatePostAbilityMovement(List<ActionPlan> actions, Ability ability, int abilitySlot, List<Unit> targets, List<UnitAI> teammates)
        {
            var reachableTiles = GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());

            foreach (var moveTile in reachableTiles.Take(difficulty + 3))
            {
                if (moveTile.occupied && moveTile.currentUnit != unit) continue;
                if (moveTile == unit.currentTile) continue;

                if (IsDirectionalAbility(ability))
                {
                    foreach (var direction in GetValidDirectionsForAbility(ability))
                    {
                        if (!WouldAbilityBeUseful(ability, unit.currentTile, direction, null, targets, teammates)) continue;
                        var plan = new ActionPlan
                        {
                            abilityToUse = ability, abilitySlot = abilitySlot,
                            aimDirection = direction, abilityFromPosition = unit.currentTile,
                            movementTarget = moveTile, isAbilityFirst = true
                        };
                        plan.CalculateScore(GetPublicProxy(), targets, teammates);
                        actions.Add(plan);
                    }
                }
                else if (ability.targeting is SingleTargeting)
                {
                    foreach (var target in targets)
                    {
                        if (!canTargetUnit(target)) continue;
                        if (!IsTargetInRangeFromPosition(ability, unit.currentTile, target.currentTile)) continue;
                        var plan = new ActionPlan
                        {
                            abilityToUse = ability, abilitySlot = abilitySlot,
                            targetTile = target.currentTile, abilityFromPosition = unit.currentTile,
                            movementTarget = moveTile, isAbilityFirst = true
                        };
                        plan.CalculateScore(GetPublicProxy(), targets, teammates);
                        actions.Add(plan);
                    }
                }
            }
        }

        private void EvaluateAbilityOptions(List<ActionPlan> actions, List<Unit> targets, List<UnitAI> teammates)
        {
            if (unit.characterData?.abilityLoadout == null) return;

            for (int i = 0; i < unit.characterData.abilityLoadout.Length; i++)
            {
                var ability = unit.characterData.abilityLoadout[i];
                if (ability == null) continue;

                EvaluateAbilityFromPosition(actions, ability, i, unit.currentTile, targets, teammates);

                var reachableTiles = GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());
                foreach (var tile in reachableTiles.Take(difficulty + 5))
                {
                    if (tile.occupied && tile.currentUnit != unit) continue;
                    EvaluateAbilityFromPosition(actions, ability, i, tile, targets, teammates, tile);
                }
            }
        }

        private void EvaluateJumpOptions(List<ActionPlan> actions, List<Unit> targets, List<UnitAI> teammates)
        {
            if (unit.GetEffectiveMovementRange() < 2) return;

            var jumpableTiles = GetJumpableTiles();

            foreach (var tile in jumpableTiles)
            {
                if (!ShouldUseJumpToTile(tile)) continue;

                var jumpOnlyPlan = new ActionPlan { isJump = true, movementTarget = tile };
                jumpOnlyPlan.CalculateScore(GetPublicProxy(), targets, teammates);
                jumpOnlyPlan.totalScore += 15f;
                actions.Add(jumpOnlyPlan);

                if (unit.characterData?.abilityLoadout == null) continue;
                for (int i = 0; i < unit.characterData.abilityLoadout.Length; i++)
                {
                    var ability = unit.characterData.abilityLoadout[i];
                    if (ability == null) continue;
                    EvaluateAbilityFromPositionForJump(actions, ability, i, tile, targets, teammates);
                }
            }
        }

        private void EvaluateMovementOptions(List<ActionPlan> actions, List<Unit> targets, List<UnitAI> teammates)
        {
            var reachableTiles = GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());
            foreach (var tile in reachableTiles)
            {
                if (tile.occupied && tile.currentUnit != unit) continue;
                var plan = new ActionPlan { movementTarget = tile };
                plan.CalculateScore(GetPublicProxy(), targets, teammates);
                actions.Add(plan);
            }
        }

        #endregion

        #region Per-Position Ability Evaluation

        private int EvaluateAbilityFromPosition(List<ActionPlan> actions, Ability ability, int abilitySlot,
            Tile fromPosition, List<Unit> targets, List<UnitAI> teammates, Tile movementTarget = null)
        {
            int optionsCreated = 0;

            if (IsDirectionalAbility(ability))
            {
                foreach (var direction in GetValidDirectionsForAbility(ability))
                {
                    if (!WouldAbilityBeUseful(ability, fromPosition, direction, null, targets, teammates)) continue;
                    var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, direction, null);
                    plan.CalculateScore(GetPublicProxy(), targets, teammates);
                    actions.Add(plan);
                    optionsCreated++;
                }
            }
            else if (ability.targeting is SingleTargeting)
            {
                if (HasTeleportEffect(ability))
                {
                    foreach (var tile in GetValidTeleportTiles(ability, fromPosition).Take(GetSearchDepthByDifficulty()))
                    {
                        if (!ShouldUseTeleportToTile(tile, fromPosition)) continue;
                        var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, tile);
                        plan.CalculateScore(GetPublicProxy(), targets, teammates);
                        actions.Add(plan);
                        optionsCreated++;
                    }
                }
                else
                {
                    foreach (var target in targets)
                    {
                        if (!canTargetUnit(target)) continue;
                        if (!IsTargetInRangeFromPosition(ability, fromPosition, target.currentTile)) continue;
                        var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, target.currentTile);
                        plan.CalculateScore(GetPublicProxy(), targets, teammates);
                        actions.Add(plan);
                        optionsCreated++;
                    }
                }
            }
            else if (ability.targeting is MultiTileSelectionTargeting multiTileTargeting)
            {
                var originalTile = unit.currentTile;
                unit.currentTile = fromPosition;
                var ctx = new AbilityContext { caster = unit, ability = ability };
                var validTiles = multiTileTargeting.GetTilesInRange(ctx);
                unit.currentTile = originalTile;

                if (validTiles.Count == 0) return 0;

                var tilesWithEnemies = validTiles.Where(t => t.currentUnit != null && !isAlly(t.currentUnit)).ToList();
                var emptyTiles = validTiles.Where(t => t.currentUnit == null).ToList();
                var chosen = tilesWithEnemies.Take(multiTileTargeting.SelectionCount).ToList();
                if (chosen.Count < multiTileTargeting.SelectionCount)
                    chosen.AddRange(emptyTiles.Take(multiTileTargeting.SelectionCount - chosen.Count));
                if (chosen.Count == 0) return 0;

                var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, null);
                plan.preSelectedTiles = chosen;
                plan.CalculateScore(GetPublicProxy(), targets, teammates);
                actions.Add(plan);
                return 1;
            }
            else
            {
                if (!WouldAbilityBeUseful(ability, fromPosition, Vector2Int.zero, null, targets, teammates)) return 0;
                var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, null);
                plan.CalculateScore(GetPublicProxy(), targets, teammates);
                actions.Add(plan);
                optionsCreated++;
            }

            return optionsCreated;
        }

        private void EvaluateAbilityFromPositionForJump(List<ActionPlan> actions, Ability ability, int abilitySlot,
            Tile jumpDestination, List<Unit> targets, List<UnitAI> teammates)
        {
            if (IsDirectionalAbility(ability))
            {
                foreach (var direction in GetValidDirectionsForAbility(ability))
                {
                    if (!WouldAbilityBeUseful(ability, jumpDestination, direction, null, targets, teammates)) continue;
                    var plan = new ActionPlan
                    {
                        isJump = true, movementTarget = jumpDestination, abilityToUse = ability,
                        abilitySlot = abilitySlot, aimDirection = direction, abilityFromPosition = jumpDestination
                    };
                    plan.CalculateScore(GetPublicProxy(), targets, teammates);
                    plan.totalScore += 20f;
                    actions.Add(plan);
                }
            }
            else if (ability.targeting is SingleTargeting && !HasTeleportEffect(ability))
            {
                foreach (var target in targets)
                {
                    if (!canTargetUnit(target)) continue;
                    if (!IsTargetInRangeFromPosition(ability, jumpDestination, target.currentTile)) continue;
                    var plan = new ActionPlan
                    {
                        isJump = true, movementTarget = jumpDestination, abilityToUse = ability,
                        abilitySlot = abilitySlot, targetTile = target.currentTile, abilityFromPosition = jumpDestination
                    };
                    plan.CalculateScore(GetPublicProxy(), targets, teammates);
                    plan.totalScore += 20f;
                    actions.Add(plan);
                }
            }
        }

        #endregion

        #region Utility Predicates

        private bool WouldAbilityBeUseful(Ability ability, Tile fromPosition, Vector2Int aimDirection,
            Tile targetTile, List<Unit> targets, List<UnitAI> teammates)
        {
            if (ability?.targeting == null) return false;

            var originalTile = unit.currentTile;
            try
            {
                unit.currentTile = fromPosition;
                var ctx = new AbilityContext { caster = unit, ability = ability, aimDir = aimDirection, targetTile = targetTile };
                var potentialTargets = ability.targeting.SelectTargets(ctx);

                if (ability.effects.Any(e => e is TeleportEffect))
                {
                    return targetTile != null && !targetTile.occupied &&
                           targetTile.passableTerrain && targetTile != fromPosition;
                }

                foreach (var target in potentialTargets)
                {
                    if (target == null) continue;
                    foreach (var effect in ability.effects)
                    {
                        if (IsEffectValidForTarget(effect, target, targets, teammates))
                            return true;
                    }
                }
                return false;
            }
            finally
            {
                unit.currentTile = originalTile;
            }
        }

        private bool IsEffectValidForTarget(AbilityEffect effect, Unit target, List<Unit> enemies, List<UnitAI> teammates)
        {
            switch (effect)
            {
                case DamageEffect _:
                case KnockbackEffect _:
                    return enemies.Contains(target);
                case StatusEffect statusEffect:
                    return IsStatusEffectValidForTarget(statusEffect, target, enemies, teammates);
                case TeleportEffect _:
                    return false;
                case MovementEffect movementEffect:
                    return !movementEffect.applyToCaster && enemies.Contains(target);
                default:
                    return enemies.Contains(target);
            }
        }

        private bool IsStatusEffectValidForTarget(StatusEffect statusEffect, Unit target, List<Unit> enemies, List<UnitAI> teammates)
        {
            foreach (var statusApp in statusEffect.statusesToApply)
            {
                if (statusApp.applyTo != StatusEffect.ApplicationTarget.Targets &&
                    statusApp.applyTo != StatusEffect.ApplicationTarget.Both)
                    continue;

                bool isDebuff = IsDebuffEffect(statusApp.statusEffectData.effectType);
                if (isDebuff && enemies.Contains(target)) return true;
                if (!isDebuff && isAlly(target)) return true;
            }
            return false;
        }

        private bool IsTargetInRangeFromPosition(Ability ability, Tile fromPosition, Tile targetTile)
        {
            if (ability.targeting is SingleTargeting singleTargeting)
            {
                var originalTile = unit.currentTile;
                unit.currentTile = fromPosition;
                var ctx = new AbilityContext { caster = unit, ability = ability };
                bool inRange = singleTargeting.IsWithinRange(ctx, targetTile);
                unit.currentTile = originalTile;
                return inRange;
            }
            return GridManager.Instance.GetGridDistance(fromPosition, targetTile) <= ability.range;
        }

        private bool ShouldUseTeleportToTile(Tile teleportDestination, Tile fromPosition)
        {
            if (teleportDestination == null || fromPosition == null) return false;
            var walkingPath = GridManager.Instance.FindPath(fromPosition, teleportDestination, unit.GetEffectiveMovementRange());
            if (walkingPath.Count == 0) return true;
            if (walkingPath.Count <= unit.GetEffectiveMovementRange()) return false;
            return walkingPath.Count > unit.GetEffectiveMovementRange();
        }

        private bool ShouldUseJumpToTile(Tile jumpDestination)
        {
            if (jumpDestination == null) return false;
            var walkingPath = GridManager.Instance.FindPath(unit.currentTile, jumpDestination, unit.GetEffectiveMovementRange());
            if (walkingPath.Count == 0 || walkingPath.Count > unit.GetEffectiveMovementRange()) return true;

            int walkingCost = walkingPath.Count;
            int jumpCost = 2;
            if (jumpCost < walkingCost)
                return IsJumpPositionTacticallyBetter(jumpDestination, walkingPath[walkingPath.Count - 1]);
            return false;
        }

        private bool IsJumpPositionTacticallyBetter(Tile jumpDestination, Tile walkDestination)
        {
            var enemies = GetAllPotentialTargetPositions();
            if (enemies.Count == 0) return false;

            float jumpAvg = enemies.Average(pos => Vector3.Distance(jumpDestination.transform.position, pos));
            float walkAvg = enemies.Average(pos => Vector3.Distance(walkDestination.transform.position, pos));
            return jumpAvg < walkAvg - 1.0f;
        }

        #endregion

        #region Helpers

        private ActionPlan CreateActionPlan(Ability ability, int abilitySlot, Tile fromPosition,
            Tile movementTarget, Vector2Int aimDirection, Tile targetTile)
        {
            return new ActionPlan
            {
                movementTarget = movementTarget, abilityToUse = ability, abilitySlot = abilitySlot,
                aimDirection = aimDirection, targetTile = targetTile, abilityFromPosition = fromPosition
            };
        }

        private List<Tile> GetJumpableTiles()
        {
            var jumpableTiles = new List<Tile>();
            Tile startTile = unit.currentTile;
            if (startTile == null) return jumpableTiles;
            const int jumpRange = 2;

            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || tile == startTile) continue;
                int distance = GridManager.Instance.GetGridDistance(startTile, tile, true);
                if (distance == jumpRange && tile.passableTerrain && !tile.occupied)
                {
                    if (!IsJumpBlockedByWalls(startTile, tile))
                        jumpableTiles.Add(tile);
                }
            }
            return jumpableTiles;
        }

        private bool IsJumpBlockedByWalls(Tile startTile, Tile targetTile)
        {
            if (startTile == null || targetTile == null) return true;
            Vector3 rayStart = startTile.transform.position + Vector3.up * 0.5f;
            Vector3 rayEnd = targetTile.transform.position + Vector3.up * 0.5f;
            float checkDistance = Vector3.Distance(rayStart, rayEnd) * 0.9f;
            int wallsLayerMask = LayerMask.GetMask("Walls");
            return Physics.Raycast(rayStart, (rayEnd - rayStart).normalized, checkDistance, wallsLayerMask);
        }

        private List<Tile> GetValidTeleportTiles(Ability ability, Tile fromPosition)
        {
            return GridManager.Instance.GetReachableTiles(fromPosition, ability.range)
                .Where(t => !t.occupied && t.passableTerrain && t != fromPosition)
                .ToList();
        }

        private List<Vector3> GetAllPotentialTargetPositions()
        {
            return UnitManager.AllUnits
                .Where(u => !(u is EnemyUnit))
                .Select(u => u.transform.position)
                .ToList();
        }

        private bool HasTeleportEffect(Ability ability) =>
            ability?.effects?.Any(e => e is TeleportEffect) ?? false;

        private bool IsDirectionalAbility(Ability ability) =>
            ability.targeting is LineTargeting ||
            ability.targeting is MovementLineTargeting ||
            (ability.targeting is AOETargeting && ability.rangeType == AbilityRangeType.Line);

        private Vector2Int[] GetValidDirectionsForAbility(Ability ability)
        {
            if (ability.targeting is LineTargeting lt && lt.horizontalOnly)
                return new[] { Vector2Int.left, Vector2Int.right };
            return new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        }

        private int GetSearchDepthByDifficulty()
        {
            switch (difficulty)
            {
                case 1: return 1; case 2: return 2; case 3: return 3; case 4: return 4;
                case 5: return 5; case 6: return 7; case 7: return 9;
                case 8: return 12; case 9: return 15; case 10: return 20;
                default: return 5;
            }
        }

        private bool IsDebuffEffect(StatusEffectType effectType)
        {
            switch (effectType)
            {
                case StatusEffectType.Bleeding: case StatusEffectType.Poison:
                case StatusEffectType.AttackDown: case StatusEffectType.DefenseDown:
                case StatusEffectType.SpeedDown: case StatusEffectType.Distracted:
                case StatusEffectType.Ensnared: case StatusEffectType.Encumbered:
                case StatusEffectType.Controlled: case StatusEffectType.Panicked:
                case StatusEffectType.Intimidated:
                    return true;
                case StatusEffectType.Taunting: return false; // buff on ally
                default: return false;
            }
        }

        /// <summary>
        /// ActionPlan.CalculateScore expects a UnitAI reference for personality/difficulty queries.
        /// This thin wrapper exposes those values without requiring the evaluator to hold a UnitAI reference.
        /// The alternative would be to refactor ActionPlan — that is a separate, lower-priority task.
        /// </summary>
        private UnitAI GetPublicProxy() => unit.GetComponent<UnitAI>();

        #endregion
    }
}