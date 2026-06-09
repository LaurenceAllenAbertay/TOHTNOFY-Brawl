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

                // Penalise self-damage abilities (e.g. RecoilDamageEffect / No Survivors).
                // Without this the AI never models the health cost, causing reckless use
                // at low health. We estimate recoil from base damage; actual recoil resolves
                // differently at runtime, but this is a fair planning-time approximation.
                foreach (var effect in abilityToUse.effects)
                {
                    if (effect is RecoilDamageEffect recoilEffect)
                    {
                        float estimatedRecoil = abilityToUse.damage * recoilEffect.recoilFraction;
                        float healthAfterRecoil = enemyUnit.currentHealth - estimatedRecoil;

                        if (healthAfterRecoil <= 0f)
                            damageScore -= 60f; // Would likely be lethal — strongly discourage.
                        else if ((float)enemyUnit.currentHealth / enemyUnit.characterData.maxHealth < 0.3f)
                            damageScore -= estimatedRecoil * 2f; // Already in danger — penalise proportionally.
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
            // For teleport plans GetEffectiveFinalTile returns the teleport destination (or the
            // post-teleport walk target for ability-first plans) rather than the walking
            // movementTarget, which is null for a pure teleport.  Without this fix every
            // teleport plan was scored as "standing still" and always lost to a walk plan.
            Tile finalPosition = GetEffectiveFinalTile(enemyUnit);
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

            // Reward positions that fall within the buff range of a teammate's support ability.
            // A unit standing where an ally can reach and buff them next turn is in a
            // strategically superior position — they can receive AttackUp, shields, heals, etc.
            // We check every ally's full reach (movement + ability range), mirroring the same
            // move-then-cast logic that CalculateDangerAtPosition uses for enemies.
            positionScore += CalculateAllyBuffProximityScore(ai, enemyUnit, finalPosition);

            // IMPORTANT: Safety consideration for final position
            // Penalize positions that leave us vulnerable after our action
            float dangerPenalty = CalculateDangerAtPosition(finalPosition, potentialTargets);
            positionScore -= dangerPenalty;

            // Penalize paths that pass through OnEnter tile hazards.
            // An otherwise safe destination is not safe if the unit will be
            // damaged or killed walking to it.
            if (movementTarget != null && enemyUnit?.currentTile != null)
            {
                // For teleport-first plans the walking portion starts from the teleport
                // destination, not the unit's current tile — adjust the origin accordingly.
                Tile walkOrigin = (IsTeleportAbilityPlan() && isAbilityFirst && targetTile != null)
                    ? targetTile
                    : enemyUnit.currentTile;

                var path = GridManager.Instance.FindPath(
                    walkOrigin,
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

            foreach (var enemy in enemies)
            {
                if (enemy?.currentTile == null) continue;

                int distanceToUs = GridManager.Instance.GetGridDistance(enemy.currentTile, position);

                foreach (var ability in UnitLoadoutManager.GetAbilities(enemy))
                {
                    if (ability == null || ability.damage <= 0) continue;
                    if (!ability.canHitEnemies) continue; // from the enemy's perspective, we are their enemy

                    // An enemy can threaten our position if they can move close enough for
                    // their ability to reach us. Combined threat radius = move range + ability range.
                    // This correctly models "walk to edge of movement, then shoot" which is exactly
                    // how the player AI and the enemy AI both operate.
                    int combinedReach = enemy.currentSpeed + ability.range + enemy.RangeModifier;

                    if (distanceToUs <= combinedReach)
                    {
                        // Scale the threat by how easily they can hit us:
                        // - If they can reach us without moving (pure ability range): full threat.
                        // - If they need to move first: slightly reduced, as movement costs their action.
                        float movementRequired = Mathf.Max(0, distanceToUs - (ability.range + enemy.RangeModifier));
                        float proximityFactor = movementRequired == 0 ? 1f : 0.75f;

                        float threat = (enemy.currentAttack + ability.damage) * proximityFactor;
                        totalDanger += threat;
                    }
                }

                // An enemy with no damage abilities can still pose a melee threat
                // via their base attack if they can walk to us directly.
                var enemyAbilities = UnitLoadoutManager.GetAbilities(enemy);
                bool hasAnyDamageAbility = enemyAbilities.Any(a => a != null && a.damage > 0 && a.canHitEnemies);
                if (!hasAnyDamageAbility && distanceToUs <= enemy.currentSpeed)
                {
                    totalDanger += enemy.currentAttack;
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

        /// <summary>
        /// Scores how well this final position is covered by teammate support abilities.
        /// For each ally, we check whether they could reach us with a buff ability on their
        /// next turn (movement range + ability range), and add a reward scaled by how
        /// valuable that buff is. This encourages the AI to cluster within support range
        /// of its most capable buffers, mirroring the move-then-cast model used throughout.
        /// </summary>
        private float CalculateAllyBuffProximityScore(UnitAI ai, EnemyUnit enemyUnit, Tile finalPosition)
        {
            if (finalPosition == null) return 0f;

            float proximityScore = 0f;

            var allEnemyUnits = UnitManager.AllUnits.OfType<EnemyUnit>()
                .Where(u => u != enemyUnit && IsAlly(ai, u))
                .ToList();

            foreach (var ally in allEnemyUnits)
            {
                if (ally?.currentTile == null) continue;

                int distanceToUs = GridManager.Instance.GetGridDistance(ally.currentTile, finalPosition);

                foreach (var ability in UnitLoadoutManager.GetAbilities(ally))
                {
                    if (ability == null) continue;
                    if (!ability.canHitAllies) continue; // Only count abilities that can target allies

                    // Combined reach = ally move range + ability range (+ any range modifier).
                    // This is the same move-then-cast model used in CalculateDangerAtPosition.
                    int combinedReach = ally.currentSpeed + ability.range + ally.RangeModifier;
                    if (distanceToUs > combinedReach) continue;

                    // Only reward abilities that actually apply buffs to allies.
                    float buffValue = 0f;
                    foreach (var effect in ability.effects)
                    {
                        if (!(effect is StatusEffect statusEffect)) continue;
                        foreach (var statusApp in statusEffect.statusesToApply)
                        {
                            if (statusApp.statusEffectData == null) continue;

                            bool appliesToUs = statusApp.applyTo == StatusEffect.ApplicationTarget.Targets
                                              || statusApp.applyTo == StatusEffect.ApplicationTarget.Both;
                            if (!appliesToUs) continue;

                            // Only count genuine buff types — debuffs on allies are not helpful.
                            if (!statusApp.statusEffectData.effectType.IsBuffType()) continue;

                            buffValue += CalculateStatusEffectValue(
                                statusApp.statusEffectData.effectType,
                                statusApp.effectPower);
                        }
                    }

                    if (buffValue <= 0f) continue;

                    // Apply the same ally-value multiplier used in CalculateStatusEffectScore
                    // so the position reward is calibrated to the same scale as the buff score.
                    float allyValue = CalculateAllyBuffValue(enemyUnit);

                    // Reduce reward slightly when the ally needs to move to reach us — they
                    // may choose a different action if a better target or position exists.
                    float movementRequired = Mathf.Max(0, distanceToUs - (ability.range + ally.RangeModifier));
                    float reachabilityFactor = movementRequired == 0 ? 1f : 0.7f;

                    proximityScore += buffValue * allyValue * reachabilityFactor;
                }
            }

            return proximityScore;
        }

        private void CalculateSafetyScore(UnitAI ai, EnemyUnit enemyUnit, List<Unit> potentialTargets)
        {
            // For teleport plans movementTarget is null, so the old `if (movementTarget == null) return`
            // caused every teleport plan to receive safetyScore = 0.  Use GetEffectiveFinalTile so
            // pure-teleport plans are evaluated at the landing tile and teleport-then-walk plans
            // are evaluated at the post-walk destination.
            Tile evalTile = GetEffectiveFinalTile(enemyUnit);
            if (evalTile == null || evalTile == enemyUnit.currentTile) return;

            Vector3 newPosition = evalTile.transform.position;

            // Start with base safety score
            safetyScore = 10f;

            // Penalty for being too close to enemies (unless we're aggressive)
            foreach (var target in potentialTargets)
            {
                if (target?.currentTile == null) continue;

                float distance = Vector3.Distance(newPosition, target.transform.position);

                // Check if target can reach us from their position
                var reachableTiles = GridManager.Instance.GetReachableTiles(target.currentTile, target.currentSpeed);
                bool canReachUs = reachableTiles.Contains(evalTile);

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
            if (IsNearEnvironmentalHazard(evalTile))
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
                if (teammateUnit == null) continue;

                foreach (var teammateAbility in UnitLoadoutManager.GetAbilities(teammateUnit))
                {
                    if (teammateAbility == null) continue;

                    if (DoesActionSetupTeammate(ai, teammate, teammateAbility))
                        teamworkScore += 5f;
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

            foreach (var effect in abilityToUse.effects)
            {
                if (!(effect is StatusEffect statusEffect)) continue;

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
                    if (statusApp.statusEffectData == null) continue;

                    float baseEffectValue = CalculateStatusEffectValue(statusApp.statusEffectData.effectType, statusApp.effectPower);
                    bool isDebuff = IsDebuff(statusApp.statusEffectData.effectType);

                    // --- Target-directed applications ---
                    if (statusApp.applyTo == StatusEffect.ApplicationTarget.Targets || statusApp.applyTo == StatusEffect.ApplicationTarget.Both)
                    {
                        foreach (var target in targets)
                        {
                            if (target == null) continue;

                            bool targetIsEnemy = potentialTargets.Contains(target);
                            bool targetIsAlly = !targetIsEnemy && target != enemyUnit;

                            if (targetIsEnemy)
                            {
                                if (isDebuff)
                                {
                                    // Debuffing a strong, healthy enemy is worth more than debuffing
                                    // one that is nearly dead. We measure threat by their current
                                    // attack stat relative to their base, then scale by health so
                                    // a near-dead unit is still worth debuffing but less so.
                                    float targetThreat = CalculateThreatWeight(target);
                                    statusEffectScore += baseEffectValue * targetThreat;
                                }
                                else
                                {
                                    // Accidentally buffing an enemy is always bad.
                                    statusEffectScore -= baseEffectValue;
                                }
                            }
                            else if (targetIsAlly)
                            {
                                if (!isDebuff)
                                {
                                    // Buffing a healthy, high-threat ally multiplies a lot of future
                                    // value — scale the score up for strong allies and down for
                                    // those near death (wasted investment).
                                    float allyValue = CalculateAllyBuffValue(target);
                                    statusEffectScore += baseEffectValue * allyValue;
                                }
                                else
                                {
                                    // Debuffing our own ally is always bad.
                                    statusEffectScore -= baseEffectValue;
                                }
                            }
                        }
                    }

                    // --- Self-cast applications ---
                    if (statusApp.applyTo == StatusEffect.ApplicationTarget.Caster || statusApp.applyTo == StatusEffect.ApplicationTarget.Both)
                    {
                        if (!isDebuff)
                        {
                            // Self-buffs scale with our own remaining health — buffing ourselves when
                            // we are nearly dead is poor value; at full health it pays off fully.
                            float selfHealthPercent = (float)enemyUnit.currentHealth / enemyUnit.characterData.maxHealth;
                            float selfHealthMultiplier = Mathf.Lerp(0.4f, 1.2f, selfHealthPercent);
                            statusEffectScore += baseEffectValue * selfHealthMultiplier;
                        }
                        else
                        {
                            // Self-inflicted debuffs are always bad.
                            statusEffectScore -= baseEffectValue;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Returns a [0.5 – 1.5] multiplier representing how much strategic value is gained
        /// by debuffing this enemy target.
        ///
        /// Uses currentAttack and currentDefense — not base stats — so buffs and debuffs
        /// already in play are reflected. A high-attack target is an immediate damage threat;
        /// a high-defense target will survive longer, meaning the debuff compounds over more
        /// turns. Both make debuffing them more worthwhile.
        /// </summary>
        private float CalculateThreatWeight(Unit target)
        {
            float healthPercent = (float)target.currentHealth / target.characterData.maxHealth;

            // currentAttack vs base: >1.0 when buffed, <1.0 when already debuffed.
            float attackWeight = target.characterData.attack > 0
                ? (float)target.currentAttack / target.characterData.attack
                : 1f;

            // currentDefense vs base: high-defense targets survive longer, so debuffs
            // on them have more turns to pay off.
            float defenseWeight = target.characterData.defense > 0
                ? (float)target.currentDefense / target.characterData.defense
                : 1f;

            // Blend all three into a 0–1 danger level, then remap to [0.5, 1.5].
            float dangerLevel = (healthPercent + Mathf.Clamp01(attackWeight) + Mathf.Clamp01(defenseWeight)) / 3f;
            return Mathf.Lerp(0.5f, 1.5f, dangerLevel);
        }

        /// <summary>
        /// Returns a [0.4 – 1.4] multiplier representing how much strategic value is gained
        /// by buffing this ally.
        ///
        /// Uses currentAttack and currentDefense — not base stats — so existing buffs and
        /// debuffs are already baked in. A high-attack ally will hit harder with the buff;
        /// a high-defense ally is more likely to survive long enough for the buff to pay off.
        /// </summary>
        private float CalculateAllyBuffValue(Unit ally)
        {
            float healthPercent = (float)ally.currentHealth / ally.characterData.maxHealth;

            // currentAttack vs base: rewards buffing allies who are already hitting hard.
            float attackWeight = ally.characterData.attack > 0
                ? (float)ally.currentAttack / ally.characterData.attack
                : 1f;

            // currentDefense vs base: a tankier ally is more likely to survive and use the buff.
            float defenseWeight = ally.characterData.defense > 0
                ? (float)ally.currentDefense / ally.characterData.defense
                : 1f;

            float allyStrength = (healthPercent + Mathf.Clamp01(attackWeight) + Mathf.Clamp01(defenseWeight)) / 3f;
            return Mathf.Lerp(0.4f, 1.4f, allyStrength);
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
            // Penalize overly complex plans for lower difficulty AIs.
            // Jump plans are never penalised — jumping is a required game mechanic, not an
            // advanced strategy, so the AI should always be willing to use it regardless of difficulty.
            bool isComplexPlan = !isJump && movementTarget != null && abilityToUse != null;

            if (isComplexPlan && difficulty < 5)
            {
                totalScore *= 0.8f; // 20% penalty for combined walk+ability plans on low difficulty
            }
        }

        #endregion

        #region Utility and Helper Methods

        private float CalculateOptimalRange(EnemyUnit enemyUnit)
        {
            float avgRange = 0f;
            int abilityCount = 0;

            foreach (var ability in UnitLoadoutManager.GetAbilities(enemyUnit))
            {
                if (ability == null) continue;
                // Exclude pure repositioning abilities (e.g. Teleport).  Their range is a
                // movement budget, not an attack range, so including them skews the ideal
                // engagement distance away from where the unit can actually deal damage.
                if (ability.effects.Any(e => e is TeleportEffect)) continue;
                avgRange += ability.range;
                abilityCount++;
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
            foreach (var ability in UnitLoadoutManager.GetAbilities(unit))
            {
                if (ability != null)
                    threat += ability.damage * 0.8f;
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

        /// <summary>True when the active ability uses <see cref="TeleportEffect"/>.</summary>
        private bool IsTeleportAbilityPlan() =>
            abilityToUse?.effects?.Any(e => e is TeleportEffect) == true;

        /// <summary>
        /// The tile where the unit will physically be standing once this full plan executes.
        /// <para>
        /// Teleport plans land on <see cref="targetTile"/>; ability-first teleport-then-walk
        /// plans continue to <see cref="movementTarget"/> after landing; all other plans use
        /// <see cref="movementTarget"/> or fall back to the unit's current tile.
        /// </para>
        /// </summary>
        private Tile GetEffectiveFinalTile(EnemyUnit enemyUnit)
        {
            if (IsTeleportAbilityPlan())
            {
                // Ability-first: teleport to targetTile, then walk to movementTarget.
                // Movement-first or pure teleport: targetTile IS the final position.
                return isAbilityFirst
                    ? (movementTarget ?? targetTile ?? enemyUnit.currentTile)
                    : (targetTile ?? movementTarget ?? enemyUnit.currentTile);
            }
            return movementTarget ?? enemyUnit.currentTile;
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
            // Base value calculation based on effect type and power.
            //
            // These values are intentionally calibrated to sit in the same scoring
            // range as a solid attack so that, before threat/ally multipliers are
            // applied, a well-chosen status move can genuinely compete with damage.
            // A typical attack scores roughly 20-40 points. Status moves should sit
            // in the 15-35 range before the context multipliers push them up or down.
            float baseValue = 0f;

            switch (effectType)
            {
                // Damage over time — value equals approximate total damage over 3 turns.
                // Raised from *3 to *4 to reflect compounding value (enemy loses actions
                // healing; AI gains free damage on future turns).
                case StatusEffectType.Bleeding:
                case StatusEffectType.Poison:
                    baseValue = effectPower * 4f;
                    break;

                // Defensive buffs — power represents HP-equivalent shielding.
                case StatusEffectType.Shielded:
                case StatusEffectType.Guarded:
                case StatusEffectType.Untargetable:
                    baseValue = effectPower * 3f;
                    break;

                // Immune: full damage negation — very high flat value.
                case StatusEffectType.Immune:
                    baseValue = 30f;
                    break;

                // Warned: can negate a full targeted attack. Slightly lower than Immune
                // since it fails against AOE and requires a free adjacent tile.
                case StatusEffectType.Warned:
                    baseValue = 22f;
                    break;

                // Stun: skip a turn entirely — among the highest tactical value possible.
                case StatusEffectType.Stunned:
                    baseValue = 35f;
                    break;

                // Hard control: Controlled and Panicked rob the enemy of their action.
                case StatusEffectType.Controlled:
                case StatusEffectType.Panicked:
                    baseValue = 30f;
                    break;

                // Stat modifiers — raised multipliers so stat swings compete with damage.
                case StatusEffectType.AttackUp:
                case StatusEffectType.AttackDown:
                    baseValue = effectPower * 3f;
                    break;

                case StatusEffectType.DefenseUp:
                case StatusEffectType.DefenseDown:
                    baseValue = effectPower * 2.5f;
                    break;

                case StatusEffectType.SpeedUp:
                case StatusEffectType.SpeedDown:
                    baseValue = effectPower * 2f;
                    break;

                // Movement denial — strong tactical value; raised to reflect positioning impact.
                case StatusEffectType.Ensnared:
                case StatusEffectType.Encumbered:
                    baseValue = 22f;
                    break;

                // Turn order manipulation — meaningful but not as decisive as full stuns.
                case StatusEffectType.Hastened:
                case StatusEffectType.Urged:
                case StatusEffectType.Distracted:
                    baseValue = 15f;
                    break;

                // Healing effects — value equals HP restored, which competes directly
                // with the damage the opponent would deal.
                case StatusEffectType.Healthy:
                case StatusEffectType.Saturated:
                    baseValue = effectPower * 2f;
                    break;

                // Alerted — reliable dodge chance; raised to reflect how much one
                // negated attack is worth.
                case StatusEffectType.Alerted:
                    baseValue = 14f;
                    break;

                // Intimidated — prevents the target from choosing us; situationally useful.
                case StatusEffectType.Intimidated:
                    baseValue = 10f;
                    break;

                // Taunting — redirects enemy AI targeting towards this ally.
                case StatusEffectType.Taunting:
                    baseValue = 12f;
                    break;

                default:
                    baseValue = effectPower * 1.5f; // Fallback for custom / future effects
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
                case StatusEffectType.Immune:
                case StatusEffectType.Warned:
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