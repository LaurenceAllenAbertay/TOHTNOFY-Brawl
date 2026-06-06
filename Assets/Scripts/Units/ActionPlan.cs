using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Represents a complete action plan for an AI turn, including movement and ability usage
    /// </summary>
    public class ActionPlan
    {
        #region Public Properties and Fields

        // Movement data
        public Tile movementTarget;
        public bool isJump = false;

        // Ability data
        public Ability abilityToUse;
        public int abilitySlot = -1;
        public Vector2Int aimDirection;
        public Tile targetTile;
        public List<Tile> preSelectedTiles; // Used for MultiTileSelectionTargeting
        public Tile abilityFromPosition; // Position to use ability from (after movement)
        public bool isAbilityFirst = false;

        // Scoring data
        public float totalScore = 0f;
        public float damageScore = 0f;
        public float positionScore = 0f;
        public float safetyScore = 0f;
        public float teamworkScore = 0f;
        public float statusEffectScore = 0f;

        #endregion

        #region Public Methods

        /// <summary>
        /// Calculates the total score for this action plan based on AI personality and difficulty
        /// </summary>
        public void CalculateScore(UnitAI ai, List<Unit> potentialTargets, List<UnitAI> teammates)
        {
            var enemyUnit = ai.GetComponent<EnemyUnit>();

            // Reset scores
            totalScore = damageScore = positionScore = safetyScore = teamworkScore = statusEffectScore = 0f;

            // Calculate individual score components
            CalculateDamageScore(ai, enemyUnit, potentialTargets);
            CalculatePositionScore(ai, enemyUnit, potentialTargets);
            CalculateSafetyScore(ai, enemyUnit, potentialTargets);
            CalculateTeamworkScore(ai, teammates);
            CalculateStatusEffectScore(ai, enemyUnit, potentialTargets, teammates);

            // Apply personality weighting
            ApplyPersonalityWeighting(ai.Personality);

            // Apply difficulty scaling
            ApplyDifficultyScaling(ai.Difficulty);

            // Calculate final total score
            totalScore = damageScore + positionScore + safetyScore + teamworkScore + statusEffectScore;

            // Apply penalty for overly complex plans on lower difficulties
            ApplyComplexityPenalty(ai.Difficulty);
        }

        #endregion

        #region Score Calculation Methods

        private void CalculateDamageScore(UnitAI ai, EnemyUnit enemyUnit, List<Unit> potentialTargets)
        {
            if (abilityToUse == null)
            {
                damageScore = 0f;
                return;
            }

            // Simulate ability execution to calculate potential damage
            var ctx = new AbilityContext
            {
                caster = enemyUnit,
                ability = abilityToUse,
                aimDir = aimDirection,
                targetTile = targetTile
            };

            // Temporarily set position for accurate targeting
            var originalTile = enemyUnit.currentTile;
            enemyUnit.currentTile = abilityFromPosition ?? enemyUnit.currentTile;

            try
            {
                var targets = abilityToUse.targeting.SelectTargets(ctx);

                foreach (var target in targets)
                {
                    if (target == null || !ai.CanTargetUnit(target)) continue;

                    // Only score damage against actual enemies
                    if (!potentialTargets.Contains(target)) continue;

                    float baseDamage = abilityToUse.damage;

                    // Bonus for targeting low-health enemies (finishing kills)
                    float healthPercent = (float)target.currentHealth / target.characterData.maxHealth;
                    if (healthPercent <= 0.3f)
                    {
                        baseDamage *= 2.5f; // Prioritize finishing off low-health targets
                    }
                    else if (healthPercent <= 0.6f)
                    {
                        baseDamage *= 1.5f; // Still valuable to damage weakened targets
                    }

                    // Bonus for targeting priority targets
                    if (ai.IsLikelyTarget(target))
                    {
                        baseDamage *= 1.3f;
                    }

                    // Bonus for targeting players vs other enemies
                    if (target is PlayerUnit)
                    {
                        baseDamage *= 1.2f;
                    }

                    damageScore += baseDamage;
                }

                // Heavy penalty for hitting allies
                foreach (var target in targets)
                {
                    if (target is EnemyUnit ally)
                    {
                        var allyAI = ally.GetComponent<UnitAI>();
                        if (allyAI != null && allyAI.TeamId == ai.TeamId)
                        {
                            damageScore -= abilityToUse.damage * 3f; // Severe penalty for friendly fire
                        }
                    }
                }
            }
            finally
            {
                enemyUnit.currentTile = originalTile;
            }
        }

        private string GetDirectionName(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return "North";
            if (direction == Vector2Int.down) return "South";
            if (direction == Vector2Int.left) return "West";
            if (direction == Vector2Int.right) return "East";
            if (direction == Vector2Int.zero) return "";
            return $"({direction.x},{direction.y})";
        }

        private void CalculatePositionScore(UnitAI ai, EnemyUnit enemyUnit, List<Unit> potentialTargets)
        {
            Tile finalPosition = movementTarget ?? enemyUnit.currentTile;
            Vector3 finalWorldPos = finalPosition.transform.position;

            // Score based on distance to targets
            foreach (var target in potentialTargets)
            {
                if (target?.currentTile == null) continue;

                float distance = Vector3.Distance(finalWorldPos, target.transform.position);

                // Optimal range depends on our abilities
                float optimalRange = CalculateOptimalRange(enemyUnit);
                float rangeDifference = Mathf.Abs(distance - optimalRange);

                // Score higher for being at optimal range
                float rangeScore = Mathf.Max(0, 10f - rangeDifference);

                // Bonus for low-health targets (want to stay close to finish them)
                float healthPercent = (float)target.currentHealth / target.characterData.maxHealth;
                if (healthPercent <= 0.3f)
                {
                    rangeScore *= 1.5f;
                }

                positionScore += rangeScore;
            }

            // Bonus for controlling key terrain
            if (IsKeyTerrain(finalPosition))
            {
                positionScore += 5f;
            }

            // IMPORTANT: Safety consideration for final position
            // Penalize positions that leave us vulnerable after our action
            float dangerPenalty = CalculateDangerAtPosition(finalPosition, potentialTargets);
            positionScore -= dangerPenalty;

            // Penalize paths that pass through OnEnter tile hazards.
            // An otherwise safe destination is not safe if the unit will be
            // damaged or killed walking to it.
            if (movementTarget != null && enemyUnit?.currentTile != null)
            {
                var path = GridManager.Instance.FindPath(
                    enemyUnit.currentTile,
                    movementTarget,
                    enemyUnit.currentSpeed);

                foreach (var tile in path)
                {
                    foreach (var effect in tile.ActiveEffects)
                    {
                        if (effect?.effectData == null) continue;
                        if (effect.effectData.triggerTiming == TriggerTiming.OnEnter)
                            positionScore -= effect.effectData.GetAIDangerValue() * (effect.effectPower / 10f);
                    }
                }
            }
        }

        private float CalculateDangerAtPosition(Tile position, List<Unit> enemies)
        {
            if (position == null) return 0f;

            float totalDanger = 0f;
            Vector3 pos = position.transform.position;
            Vector3 tileSpacing = GridManager.Instance.GetTileSpacing();

            foreach (var enemy in enemies)
            {
                if (enemy?.currentTile == null) continue;

                // Check if enemy can reach this position
                var enemyReachableTiles = GridManager.Instance.GetReachableTiles(enemy.currentTile, enemy.currentSpeed);
                bool canReachUs = enemyReachableTiles.Contains(position);

                if (canReachUs)
                {
                    float threat = enemy.currentAttack;

                    // Add ability threat
                    if (enemy.characterData?.abilityLoadout != null)
                    {
                        foreach (var ability in enemy.characterData.abilityLoadout)
                        {
                            if (ability != null)
                            {
                                threat += ability.damage * 0.5f;
                            }
                        }
                    }

                    totalDanger += threat;
                }
                else
                {
                    // Check if enemy can hit us with ranged abilities
                    if (enemy.characterData?.abilityLoadout != null)
                    {
                        foreach (var ability in enemy.characterData.abilityLoadout)
                        {
                            if (ability != null && ability.range > 1)
                            {
                                float distance = Vector3.Distance(pos, enemy.transform.position);
                                // Use horizontal spacing (X or Z) for range calculations
                                float rangeInWorldUnits = ability.range * tileSpacing.x;

                                if (distance <= rangeInWorldUnits)
                                {
                                    totalDanger += ability.damage * 0.3f; // Ranged threat
                                }
                            }
                        }
                    }
                }
            }

            // Factor in persistent tile hazards — AI avoids standing in fire, poison clouds, etc.
            // GetAIDangerValue() is defined per-effect so each hazard type self-reports its threat.
            // effectPower / 10f normalises it to the same scale as the ability threat values above.
            foreach (var effect in position.ActiveEffects)
            {
                if (effect?.effectData == null) continue;
                totalDanger += effect.effectData.GetAIDangerValue() * (effect.effectPower / 10f);
            }

            return totalDanger;
        }

        private void CalculateSafetyScore(UnitAI ai, EnemyUnit enemyUnit, List<Unit> potentialTargets)
        {
            if (movementTarget == null) return;

            Vector3 newPosition = movementTarget.transform.position;

            // Start with base safety score
            safetyScore = 10f;

            // Penalty for being too close to enemies (unless we're aggressive)
            foreach (var target in potentialTargets)
            {
                if (target?.currentTile == null) continue;

                float distance = Vector3.Distance(newPosition, target.transform.position);

                // Check if target can reach us from their position
                var reachableTiles = GridManager.Instance.GetReachableTiles(target.currentTile, target.currentSpeed);
                bool canReachUs = reachableTiles.Contains(movementTarget);

                if (canReachUs)
                {
                    // Calculate potential threat from this target
                    float threat = CalculateThreatFromUnit(target);
                    safetyScore -= threat;
                }
            }

            // Bonus for having allies nearby (safety in numbers)
            var teammates = UnitManager.AllUnits.OfType<EnemyUnit>()
                .Where(u => u != enemyUnit && IsAlly(ai, u))
                .ToList();

            foreach (var teammate in teammates)
            {
                float distance = Vector3.Distance(newPosition, teammate.transform.position);
                if (distance <= 3f) // Within 3 tiles
                {
                    safetyScore += 2f;
                }
            }

            // Penalty for being in dangerous terrain or near environmental hazards
            if (IsNearEnvironmentalHazard(movementTarget))
            {
                safetyScore -= 8f;
            }
        }

        private void CalculateTeamworkScore(UnitAI ai, List<UnitAI> teammates)
        {
            if (teammates.Count == 0 || ai.Difficulty < 6) return; // Only higher difficulty AIs coordinate

            // Bonus for setting up teammate abilities
            foreach (var teammate in teammates)
            {
                var teammateUnit = teammate.GetComponent<EnemyUnit>();
                if (teammateUnit?.characterData?.abilityLoadout == null) continue;

                foreach (var teammateAbility in teammateUnit.characterData.abilityLoadout)
                {
                    if (teammateAbility == null) continue;

                    // Check if our action sets up a good position for teammate's ability
                    if (DoesActionSetupTeammate(ai, teammate, teammateAbility))
                    {
                        teamworkScore += 5f;
                    }
                }
            }

            // Bonus for coordinated positioning
            if (IsGoodCoordinatedPosition(ai, teammates))
            {
                teamworkScore += 3f;
            }
        }

        private void CalculateStatusEffectScore(UnitAI ai, EnemyUnit enemyUnit, List<Unit> potentialTargets, List<UnitAI> teammates)
        {
            if (abilityToUse == null) return;

            // Check if ability applies status effects
            foreach (var effect in abilityToUse.effects)
            {
                if (effect is StatusEffect statusEffect)
                {
                    var ctx = new AbilityContext
                    {
                        caster = enemyUnit,
                        ability = abilityToUse,
                        aimDir = aimDirection,
                        targetTile = targetTile
                    };

                    var targets = abilityToUse.targeting.SelectTargets(ctx);

                    foreach (var statusApp in statusEffect.statusesToApply)
                    {
                        float effectValue = CalculateStatusEffectValue(statusApp.statusEffectData.effectType, statusApp.effectPower);

                        // Apply to appropriate targets based on effect type
                        if (statusApp.applyTo == StatusEffect.ApplicationTarget.Targets || statusApp.applyTo == StatusEffect.ApplicationTarget.Both)
                        {
                            foreach (var target in targets)
                            {
                                if (IsDebuff(statusApp.statusEffectData.effectType))
                                {
                                    // Debuffs are good on enemies
                                    if (potentialTargets.Contains(target))
                                        statusEffectScore += effectValue;
                                }
                                else
                                {
                                    // Buffs are bad on enemies
                                    if (potentialTargets.Contains(target))
                                        statusEffectScore -= effectValue;
                                }
                            }
                        }

                        if (statusApp.applyTo == StatusEffect.ApplicationTarget.Caster || statusApp.applyTo == StatusEffect.ApplicationTarget.Both)
                        {
                            // Self-buffs are generally good
                            if (!IsDebuff(statusApp.statusEffectData.effectType))
                                statusEffectScore += effectValue;
                        }
                    }
                }
            }
        }

        #endregion

        #region Score Modification Methods

        private void ApplyPersonalityWeighting(UnitAI.AIPersonality personality)
        {
            switch (personality)
            {
                case UnitAI.AIPersonality.Aggressive:
                    damageScore *= 1.5f;
                    safetyScore *= 0.7f;
                    statusEffectScore *= 1.2f; // Prioritize damage-dealing effects
                    break;

                case UnitAI.AIPersonality.Defensive:
                    safetyScore *= 1.8f;
                    positionScore *= 1.3f;
                    damageScore *= 0.8f;
                    break;

                case UnitAI.AIPersonality.Supportive:
                    teamworkScore *= 2.0f;
                    statusEffectScore *= 1.4f; // Prioritize buffs/healing
                    damageScore *= 0.6f;
                    break;

                case UnitAI.AIPersonality.Balanced:
                    // No modifications - even weighting
                    break;
            }
        }

        private void ApplyDifficultyScaling(int difficulty)
        {
            // Much more granular difficulty scaling
            float difficultyMultiplier = difficulty / 10f; // 0.1 to 1.0

            switch (difficulty)
            {
                case 1: // Braindead
                    damageScore *= 1.5f;
                    teamworkScore = 0f;
                    statusEffectScore *= 0.1f;
                    positionScore *= 0.3f;
                    safetyScore *= 0.4f;
                    break;

                case 2: // Very Easy
                    damageScore *= 1.3f;
                    teamworkScore *= 0.1f;
                    statusEffectScore *= 0.2f;
                    positionScore *= 0.4f;
                    safetyScore *= 0.5f;
                    break;

                case 3: // Easy
                    damageScore *= 1.2f;
                    teamworkScore *= 0.2f;
                    statusEffectScore *= 0.3f;
                    positionScore *= 0.5f;
                    safetyScore *= 0.6f;
                    break;

                case 4: // Easy-Medium
                    damageScore *= 1.1f;
                    teamworkScore *= 0.4f;
                    statusEffectScore *= 0.5f;
                    positionScore *= 0.7f;
                    safetyScore *= 0.7f;
                    break;

                case 5: // Medium
                    damageScore *= 1.0f;
                    teamworkScore *= 0.6f;
                    statusEffectScore *= 0.7f;
                    positionScore *= 0.8f;
                    safetyScore *= 0.8f;
                    break;

                case 6: // Medium-Hard
                    damageScore *= 1.0f;
                    teamworkScore *= 0.8f;
                    statusEffectScore *= 0.9f;
                    positionScore *= 1.0f;
                    safetyScore *= 0.9f;
                    break;

                case 7: // Hard
                    damageScore *= 0.9f;
                    teamworkScore *= 1.0f;
                    statusEffectScore *= 1.1f;
                    positionScore *= 1.2f;
                    safetyScore *= 1.1f;
                    break;

                case 8: // Very Hard
                    damageScore *= 0.8f;
                    teamworkScore *= 1.2f;
                    statusEffectScore *= 1.3f;
                    positionScore *= 1.4f;
                    safetyScore *= 1.3f;
                    break;

                case 9: // Expert
                    damageScore *= 0.7f;
                    teamworkScore *= 1.4f;
                    statusEffectScore *= 1.5f;
                    positionScore *= 1.6f;
                    safetyScore *= 1.5f;
                    break;

                case 10: // Master
                    damageScore *= 0.6f;
                    teamworkScore *= 1.6f;
                    statusEffectScore *= 1.7f;
                    positionScore *= 1.8f;
                    safetyScore *= 1.7f;
                    break;
            }
        }

        private void ApplyComplexityPenalty(int difficulty)
        {
            // Penalize overly complex plans for lower difficulty AIs
            bool isComplexPlan = (movementTarget != null && abilityToUse != null) || isJump;

            if (isComplexPlan && difficulty < 5)
            {
                totalScore *= 0.8f; // 20% penalty for complex plans on low difficulty
            }
        }

        #endregion

        #region Utility and Helper Methods

        private float CalculateOptimalRange(EnemyUnit enemyUnit)
        {
            if (enemyUnit.characterData?.abilityLoadout == null) return 2f;

            float avgRange = 0f;
            int abilityCount = 0;

            foreach (var ability in enemyUnit.characterData.abilityLoadout)
            {
                if (ability != null)
                {
                    avgRange += ability.range;
                    abilityCount++;
                }
            }

            return abilityCount > 0 ? avgRange / abilityCount : 2f;
        }

        private bool IsKeyTerrain(Tile tile)
        {
            // Simple heuristic: tiles near center of map are more valuable
            var allTiles = GridManager.Instance.AllTiles;
            if (allTiles.Count == 0) return false;

            Vector3 mapCenter = Vector3.zero;
            foreach (var t in allTiles)
            {
                mapCenter += t.transform.position;
            }
            mapCenter /= allTiles.Count;

            float distanceToCenter = Vector3.Distance(tile.transform.position, mapCenter);
            return distanceToCenter <= 3f; // Within 3 tiles of center
        }

        private float CalculateThreatFromUnit(Unit unit)
        {
            float threat = 0f;

            // Base threat from unit's attack stat
            threat += unit.currentAttack;

            // Additional threat from abilities
            if (unit.characterData?.abilityLoadout != null)
            {
                foreach (var ability in unit.characterData.abilityLoadout)
                {
                    if (ability != null)
                    {
                        threat += ability.damage * 0.8f; // Potential ability damage
                    }
                }
            }

            // Reduce threat based on unit's current health (wounded units are less threatening)
            float healthPercent = (float)unit.currentHealth / unit.characterData.maxHealth;
            threat *= healthPercent;

            return threat;
        }

        private bool IsAlly(UnitAI ai, EnemyUnit otherUnit)
        {
            var otherAI = otherUnit.GetComponent<UnitAI>();
            return otherAI != null && otherAI.TeamId == ai.TeamId;
        }

        private bool IsNearEnvironmentalHazard(Tile tile)
        {
            // Check for nearby impassable terrain that could trap the unit
            var adjacentTiles = GridManager.Instance.GetAdjacentTiles(tile, true); // Include diagonals
            int blockedSides = 0;

            foreach (var adjacent in adjacentTiles)
            {
                if (adjacent == null || !adjacent.passableTerrain)
                {
                    blockedSides++;
                }
            }

            // If more than half the adjacent tiles are blocked, it's potentially dangerous
            return blockedSides > 4;
        }

        private float CalculateStatusEffectValue(StatusEffectType effectType, float effectPower)
        {
            // Base value calculation based on effect type and power
            float baseValue = 0f;

            switch (effectType)
            {
                // Damage over time effects - value based on total potential damage
                case StatusEffectType.Bleeding:
                case StatusEffectType.Poison:
                    baseValue = effectPower * 3f; // Assume average 3 turn duration
                    break;

                // Defensive buffs - high value for survivability
                case StatusEffectType.Shielded:
                case StatusEffectType.Guarded:
                case StatusEffectType.Untargetable:
                    baseValue = effectPower * 2.5f;
                    break;

                // Stat modifiers - value based on stat impact
                case StatusEffectType.AttackUp:
                case StatusEffectType.AttackDown:
                    baseValue = effectPower * 2f;
                    break;

                case StatusEffectType.DefenseUp:
                case StatusEffectType.DefenseDown:
                    baseValue = effectPower * 1.8f;
                    break;

                case StatusEffectType.SpeedUp:
                case StatusEffectType.SpeedDown:
                    baseValue = effectPower * 1.5f;
                    break;

                // Movement control - high tactical value
                case StatusEffectType.Ensnared:
                case StatusEffectType.Encumbered:
                    baseValue = 15f; // Flat high value for movement denial
                    break;

                // Turn order manipulation
                case StatusEffectType.Hastened:
                case StatusEffectType.Urged:
                case StatusEffectType.Distracted:
                    baseValue = 10f;
                    break;

                // Healing effects
                case StatusEffectType.Healthy:
                case StatusEffectType.Saturated:
                    baseValue = effectPower * 1.5f;
                    break;

                // Control effects - very high value
                case StatusEffectType.Controlled:
                case StatusEffectType.Panicked:
                    baseValue = 20f;
                    break;

                case StatusEffectType.Alerted:
                    baseValue = 8f; // Dodge chance is valuable
                    break;

                default:
                    baseValue = effectPower; // Fallback for custom effects
                    break;
            }

            return baseValue;
        }

        private bool IsDebuff(StatusEffectType effectType)
        {
            // Returns true for negative effects that you want to apply to enemies
            // Returns false for positive effects that you want to apply to allies/self

            switch (effectType)
            {
                // Debuffs (negative effects)
                case StatusEffectType.Bleeding:
                case StatusEffectType.Poison:
                case StatusEffectType.AttackDown:
                case StatusEffectType.DefenseDown:
                case StatusEffectType.SpeedDown:
                case StatusEffectType.Distracted:
                case StatusEffectType.Ensnared:
                case StatusEffectType.Encumbered:
                case StatusEffectType.Controlled:
                case StatusEffectType.Panicked:
                case StatusEffectType.Intimidated:
                case StatusEffectType.Stunned:
                    return true;

                // Taunting is a buff applied to an ally, not a debuff on an enemy
                case StatusEffectType.Taunting:
                    return false;

                // Buffs (positive effects)
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
                    return false;

                default:
                    return true;
            }
        }

        #endregion

        #region Teamwork and Coordination Methods

        private bool DoesActionSetupTeammate(UnitAI ai, UnitAI teammate, Ability teammateAbility)
        {
            // Check if our action (especially knockback/movement effects) positions enemies 
            // optimally for teammate's abilities

            if (abilityToUse == null) return false;

            // Look for knockback effects that could set up line attacks
            foreach (var effect in abilityToUse.effects)
            {
                if (effect is KnockbackEffect)
                {
                    // If teammate has line abilities, knockback could set up a line
                    if (teammateAbility.targeting is LineTargeting)
                    {
                        return true; // Simplified - in practice, check actual positioning
                    }
                }
            }

            return false;
        }

        private bool IsGoodCoordinatedPosition(UnitAI ai, List<UnitAI> teammates)
        {
            if (movementTarget == null) return false;

            // Check if our position forms a good formation with teammates
            Vector3 ourPosition = movementTarget.transform.position;

            foreach (var teammate in teammates)
            {
                var teammateUnit = teammate.GetComponent<EnemyUnit>();
                if (teammateUnit?.currentTile == null) continue;

                float distance = Vector3.Distance(ourPosition, teammateUnit.transform.position);

                // Good coordination: close enough to support, far enough to avoid AOE
                if (distance >= 2f && distance <= 4f)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}