using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitAI : MonoBehaviour
    {
        #region Enums and Data Structures

        [System.Serializable]
        public enum AIPersonality
        {
            Aggressive,    // Prioritizes dealing damage
            Defensive,     // Prioritizes survival and positioning
            Supportive,    // Prioritizes helping allies
            Balanced       // Even mix of all behaviors
        }

        #endregion

        #region Inspector Settings

        [Header("AI Configuration")]
        [Range(1, 10)]
        [SerializeField] private int difficulty = 5;
        [SerializeField] private AIPersonality personality = AIPersonality.Balanced;
        [SerializeField] private int teamId = 1;
        [SerializeField] private bool hostileToAllNonTeam = false; // If false, only hostile to players

        [Header("Debug Settings")]
        [SerializeField] private bool enableDebugLogging = true;
        [SerializeField] private bool logDetailedScoring = false;
        [SerializeField] private int topActionsToLog = 3;

        [Header("Timing")]
        [SerializeField] private float thinkingDelay = 1.5f;  // Pause before evaluating actions
        [SerializeField] private float actionDelay = 0.8f;    // Pause between movement and abilities
        [SerializeField] private float endTurnDelay = 1.0f;   // Pause before ending turn

        #endregion

        #region Private Fields

        // Core components
        private Unit unit;
        private TurnManager turnManager;
        private JumpSystem jumpSystem;

        // Decision making data
        private List<ActionPlan> evaluatedActions = new List<ActionPlan>();
        private ActionPlan selectedPlan;

        // Targeting overrides - units that temporarily can't be targeted or should be prioritized
        private Dictionary<Unit, int> untargetableUnits = new Dictionary<Unit, int>();
        private Dictionary<Unit, int> targetLikelyUnits = new Dictionary<Unit, int>();

        // Team coordination - static dictionary shared across all AI units
        private static Dictionary<int, List<UnitAI>> teamGroups = new Dictionary<int, List<UnitAI>>();

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            unit = GetComponent<Unit>();
            if (unit == null)
            {
                Debug.LogError($"UnitAI on {gameObject.name} requires a Unit component!");
                enabled = false;
                return;
            }

            RegisterWithTeam();

            // Subscribe to events early to ensure we catch TurnManager.Start() calls
            TurnManager.OnTurnStarted += OnTurnStarted;
            TurnManager.OnTurnEnded += OnTurnEnded;

            if (enableDebugLogging)
            {
                Debug.Log($"[{gameObject.name}] UnitAI initialized and subscribed to events");
            }
        }

        void Start()
        {
            turnManager = FindObjectOfType<TurnManager>();
            jumpSystem = FindObjectOfType<JumpSystem>();

            if (enableDebugLogging)
            {
                Debug.Log($"[{gameObject.name}] UnitAI Start() complete, turnManager found: {turnManager != null}");
            }
        }

        void OnDestroy()
        {
            UnregisterFromTeam();
            TurnManager.OnTurnStarted -= OnTurnStarted;
            TurnManager.OnTurnEnded -= OnTurnEnded;
        }

        #endregion

        #region Team Management

        private void RegisterWithTeam()
        {
            if (!teamGroups.ContainsKey(teamId))
                teamGroups[teamId] = new List<UnitAI>();

            if (!teamGroups[teamId].Contains(this))
                teamGroups[teamId].Add(this);
        }

        private void UnregisterFromTeam()
        {
            if (teamGroups.ContainsKey(teamId))
            {
                teamGroups[teamId].Remove(this);
                if (teamGroups[teamId].Count == 0)
                    teamGroups.Remove(teamId);
            }
        }

        private List<UnitAI> GetTeammates()
        {
            return teamGroups.ContainsKey(teamId) ?
                teamGroups[teamId].Where(ai => ai != this && ai != null).ToList() :
                new List<UnitAI>();
        }

        #endregion

        #region Turn Management

        private void OnTurnStarted(Unit _unit)
        {
            if (_unit == unit)
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[{gameObject.name}] Starting AI turn execution");
                }
                StartCoroutine(ExecuteAITurn());
            }
        }

        private void OnTurnEnded(Unit _unit)
        {
            if (_unit == unit)
            {
                UpdateTargetingDurations();
            }
        }

        /// <summary>
        /// Main AI turn execution coroutine - handles the complete turn flow
        /// </summary>
        private IEnumerator ExecuteAITurn()
        {
            LogTurnStart();

            // Give player time to orient to the AI's turn
            yield return new WaitForSeconds(thinkingDelay);

            // Generate and evaluate all possible actions
            LogEvaluationStart();
            EvaluateAllActions();
            LogEvaluationResults();

            // Select best action based on difficulty and personality
            selectedPlan = SelectBestAction();
            LogSelectedAction();

            if (selectedPlan != null)
            {
                yield return StartCoroutine(ExecuteActionPlan(selectedPlan));
            }
            else
            {
                LogNoValidActions();
                yield return new WaitForSeconds(0.5f);
            }

            LogTurnEnd();

            // Final pause before ending turn
            yield return new WaitForSeconds(endTurnDelay);

            turnManager.EndTurn();
        }

        #endregion

        #region Action Evaluation - Core System

        /// <summary>
        /// Main evaluation method - generates all possible action combinations
        /// Evaluates in order of effectiveness: ability-first, move-then-ability, jump combos, movement-only
        /// </summary>
        private void EvaluateAllActions()
        {
            evaluatedActions.Clear();

            var potentialTargets = GetPotentialTargets();
            var teammates = GetTeammates();

            LogTargetsAndTeammates(potentialTargets, teammates);

            // Strategy 1: Use abilities from current position first, then consider follow-up movement
            EvaluateAbilityFirstStrategies(potentialTargets, teammates);

            // Strategy 2: Move to better positions, then use abilities
            EvaluateAbilityOptions(potentialTargets, teammates);

            // Strategy 3: Jump combinations (high value since you can still act after jumping)
            EvaluateJumpOptions(potentialTargets, teammates);

            // Strategy 4: Movement-only fallback options
            EvaluateMovementOptions(potentialTargets, teammates);

            // Sort by score for consistent selection logic
            evaluatedActions = evaluatedActions.OrderByDescending(a => a.totalScore).ToList();
        }

        /// <summary>
        /// Evaluates using abilities from current position, then moving afterward
        /// This is often optimal since abilities may change the tactical situation
        /// </summary>
        private void EvaluateAbilityFirstStrategies(List<Unit> targets, List<UnitAI> teammates)
        {
            if (unit.characterData?.abilityLoadout == null) return;

            int abilityFirstOptionsEvaluated = 0;

            for (int i = 0; i < unit.characterData.abilityLoadout.Length; i++)
            {
                var ability = unit.characterData.abilityLoadout[i];
                if (ability == null) continue;

                // Check if ability can be used effectively from current position
                int optionsForThisAbility = EvaluateAbilityFromPosition(ability, i, unit.currentTile, targets, teammates);

                // If ability is usable, consider movement options afterward
                if (optionsForThisAbility > 0)
                {
                    EvaluatePostAbilityMovement(ability, i, targets, teammates);
                }

                abilityFirstOptionsEvaluated += optionsForThisAbility;
            }

            if (enableDebugLogging && abilityFirstOptionsEvaluated > 0)
            {
                Debug.Log($"[{unit.name}] Evaluated {abilityFirstOptionsEvaluated} ability-first strategies");
            }
        }

        /// <summary>
        /// For abilities used from current position, evaluate potential follow-up movement
        /// </summary>
        private void EvaluatePostAbilityMovement(Ability ability, int abilitySlot, List<Unit> targets, List<UnitAI> teammates)
        {
            var reachableTiles = GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());

            // Limit search based on difficulty to prevent analysis paralysis at low difficulties
            foreach (var moveTile in reachableTiles.Take(difficulty + 3))
            {
                if (moveTile.occupied && moveTile.currentUnit != unit) continue;
                if (moveTile == unit.currentTile) continue; // Already considered ability-only

                // Create ability-first-then-move plans
                if (IsDirectionalAbility(ability))
                {
                    Vector2Int[] directions = GetValidDirectionsForAbility(ability);
                    foreach (var direction in directions)
                    {
                        if (!WouldAbilityBeUseful(ability, unit.currentTile, direction, null, targets, teammates))
                            continue;

                        var plan = new ActionPlan
                        {
                            abilityToUse = ability,
                            abilitySlot = abilitySlot,
                            aimDirection = direction,
                            abilityFromPosition = unit.currentTile,
                            movementTarget = moveTile,
                            isAbilityFirst = true
                        };
                        plan.CalculateScore(this, targets, teammates);
                        evaluatedActions.Add(plan);
                    }
                }
                else if (ability.targeting is SingleTargeting)
                {
                    foreach (var target in targets)
                    {
                        if (!CanTargetUnit(target)) continue;
                        if (!IsTargetInRangeFromPosition(ability, unit.currentTile, target.currentTile)) continue;

                        var plan = new ActionPlan
                        {
                            abilityToUse = ability,
                            abilitySlot = abilitySlot,
                            targetTile = target.currentTile,
                            abilityFromPosition = unit.currentTile,
                            movementTarget = moveTile,
                            isAbilityFirst = true
                        };
                        plan.CalculateScore(this, targets, teammates);
                        evaluatedActions.Add(plan);
                    }
                }
            }
        }

        /// <summary>
        /// Evaluates movement-then-ability combinations and ability-only options
        /// </summary>
        private void EvaluateAbilityOptions(List<Unit> targets, List<UnitAI> teammates)
        {
            if (unit.characterData?.abilityLoadout == null) return;

            int abilityOptionsEvaluated = 0;

            for (int i = 0; i < unit.characterData.abilityLoadout.Length; i++)
            {
                var ability = unit.characterData.abilityLoadout[i];
                if (ability == null) continue;

                int optionsForThisAbility = 0;

                // Evaluate using ability from current position (ability-only option)
                optionsForThisAbility += EvaluateAbilityFromPosition(ability, i, unit.currentTile, targets, teammates);

                // Evaluate using ability from different positions after moving
                var reachableTiles = GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());

                foreach (var tile in reachableTiles.Take(difficulty + 5))
                {
                    if (tile.occupied && tile.currentUnit != unit) continue;
                    optionsForThisAbility += EvaluateAbilityFromPosition(ability, i, tile, targets, teammates, tile);
                }

                abilityOptionsEvaluated += optionsForThisAbility;

                if (enableDebugLogging)
                {
                    Debug.Log($"[{unit.name}] Evaluated {optionsForThisAbility} options for ability '{ability.abilityName}'");
                }
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[{unit.name}] Total ability options evaluated: {abilityOptionsEvaluated}");
            }
        }

        /// <summary>
        /// Core ability evaluation - creates ActionPlan objects for all valid uses of an ability from a position
        /// </summary>
        private int EvaluateAbilityFromPosition(Ability ability, int abilitySlot, Tile fromPosition, List<Unit> targets, List<UnitAI> teammates, Tile movementTarget = null)
        {
            int optionsCreated = 0;

            if (IsDirectionalAbility(ability))
            {
                Vector2Int[] directions = GetValidDirectionsForAbility(ability);

                foreach (var direction in directions)
                {
                    // Pre-validate that this direction would actually accomplish something
                    if (!WouldAbilityBeUseful(ability, fromPosition, direction, null, targets, teammates))
                        continue;

                    var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, direction, null);
                    plan.CalculateScore(this, targets, teammates);
                    evaluatedActions.Add(plan);
                    optionsCreated++;
                }
            }
            else if (ability.targeting is SingleTargeting)
            {
                // Special handling for teleport abilities - they target empty tiles
                if (HasTeleportEffect(ability))
                {
                    var validTeleportTiles = GetValidTeleportTiles(ability, fromPosition);
                    foreach (var tile in validTeleportTiles.Take(GetSearchDepthByDifficulty()))
                    {
                        // FIXED: Only consider teleport if it's more efficient than walking
                        if (!ShouldUseTeleportToTile(tile, fromPosition))
                            continue;

                        var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, tile);
                        plan.CalculateScore(this, targets, teammates);
                        evaluatedActions.Add(plan);
                        optionsCreated++;
                    }
                }
                else
                {
                    // Regular single-target abilities target units
                    foreach (var target in targets)
                    {
                        if (!CanTargetUnit(target)) continue;
                        if (!IsTargetInRangeFromPosition(ability, fromPosition, target.currentTile)) continue;

                        var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, target.currentTile);
                        plan.CalculateScore(this, targets, teammates);
                        evaluatedActions.Add(plan);
                        optionsCreated++;
                    }
                }
            }
            else
            {
                // Area abilities (AOE, Random AOE, etc.) - check if they would be useful
                if (!WouldAbilityBeUseful(ability, fromPosition, Vector2Int.zero, null, targets, teammates))
                    return 0;

                var plan = CreateActionPlan(ability, abilitySlot, fromPosition, movementTarget, Vector2Int.zero, null);
                plan.CalculateScore(this, targets, teammates);
                evaluatedActions.Add(plan);
                optionsCreated++;
            }

            return optionsCreated;
        }

        /// <summary>
        /// Factory method for creating ActionPlan objects with consistent initialization
        /// </summary>
        private ActionPlan CreateActionPlan(Ability ability, int abilitySlot, Tile fromPosition, Tile movementTarget, Vector2Int aimDirection, Tile targetTile)
        {
            return new ActionPlan
            {
                movementTarget = movementTarget,
                abilityToUse = ability,
                abilitySlot = abilitySlot,
                aimDirection = aimDirection,
                targetTile = targetTile,
                abilityFromPosition = fromPosition
            };
        }

        /// <summary>
        /// Evaluates jump-based strategies - jumping 2 tiles in cardinal directions
        /// Jumps are valuable because you can still act after jumping
        /// </summary>
        private void EvaluateJumpOptions(List<Unit> targets, List<UnitAI> teammates)
        {
            if (unit.GetEffectiveMovementRange() < 2) return; // Need 2 movement for jump

            var jumpableTiles = GetJumpableTiles();
            int jumpOptionsEvaluated = jumpableTiles.Count;

            foreach (var tile in jumpableTiles)
            {
                // FIXED: Only consider jump if it's more efficient than walking
                if (!ShouldUseJumpToTile(tile))
                    continue;

                // Jump-only option
                var jumpOnlyPlan = new ActionPlan
                {
                    isJump = true,
                    movementTarget = tile
                };
                jumpOnlyPlan.CalculateScore(this, targets, teammates);
                jumpOnlyPlan.totalScore += 15f; // Bonus for mobility after jump
                evaluatedActions.Add(jumpOnlyPlan);

                // Jump + ability combinations
                if (unit.characterData?.abilityLoadout != null)
                {
                    for (int i = 0; i < unit.characterData.abilityLoadout.Length; i++)
                    {
                        var ability = unit.characterData.abilityLoadout[i];
                        if (ability == null) continue;

                        int abilityOptions = EvaluateAbilityFromPositionForJump(ability, i, tile, targets, teammates);
                        jumpOptionsEvaluated += abilityOptions;
                    }
                }
            }

            if (enableDebugLogging && jumpOptionsEvaluated > 0)
            {
                Debug.Log($"[{unit.name}] Evaluated {jumpOptionsEvaluated} jump options");
            }
        }

        /// <summary>
        /// Determines if teleporting to a tile is more efficient than walking there
        /// Only recommends teleport if walking would take longer or is impossible
        /// </summary>
        private bool ShouldUseTeleportToTile(Tile teleportDestination, Tile fromPosition)
        {
            if (teleportDestination == null || fromPosition == null) return false;

            // Always allow teleport if we can't walk there at all
            var walkingPath = GridManager.Instance.FindPath(fromPosition, teleportDestination, unit.GetEffectiveMovementRange());
            if (walkingPath.Count == 0)
                return true; // Can't walk there, so teleport is valid

            // Don't teleport if we can walk there in the same turn
            if (walkingPath.Count <= unit.GetEffectiveMovementRange())
                return false; // Walking is just as efficient

            // Allow teleport if walking would take more than 1 turn
            return walkingPath.Count > unit.GetEffectiveMovementRange();
        }

        /// <summary>
        /// Determines if jumping to a tile is more efficient than walking there
        /// Only recommends jump if it provides tactical advantage over normal movement
        /// </summary>
        private bool ShouldUseJumpToTile(Tile jumpDestination)
        {
            if (jumpDestination == null) return false;

            // Check if we can walk there with current movement
            var walkingPath = GridManager.Instance.FindPath(unit.currentTile, jumpDestination, unit.GetEffectiveMovementRange());

            // If we can't walk there this turn, jumping is valuable
            if (walkingPath.Count == 0 || walkingPath.Count > unit.GetEffectiveMovementRange())
                return true;

            // If we can walk there, only jump if it saves significant movement points
            // This preserves movement for potential follow-up actions
            int walkingCost = walkingPath.Count;
            int jumpCost = 2; // Jumping always costs 2 movement

            // Only jump if it saves at least 1 movement point AND gives tactical positioning
            if (jumpCost < walkingCost)
            {
                // Additional check: is the jump destination tactically superior?
                // (e.g., better positioning relative to enemies/allies)
                return IsJumpPositionTacticallyBetter(jumpDestination, walkingPath[walkingPath.Count - 1]);
            }

            return false; // Walking is more efficient
        }

        /// <summary>
        /// Evaluates if a jump destination provides better tactical positioning than the walking destination
        /// </summary>
        private bool IsJumpPositionTacticallyBetter(Tile jumpDestination, Tile walkDestination)
        {
            // For now, simple heuristic: jump is better if it gets us closer to enemies on average
            var enemies = GetPotentialTargets();
            if (enemies.Count == 0) return false;

            float jumpAvgDistance = enemies.Average(enemy =>
                Vector3.Distance(jumpDestination.transform.position, enemy.transform.position));
            float walkAvgDistance = enemies.Average(enemy =>
                Vector3.Distance(walkDestination.transform.position, enemy.transform.position));

            // Jump is tactically better if it gets us noticeably closer to enemies
            return jumpAvgDistance < walkAvgDistance - 1.0f; // At least 1 unit closer on average
        }

        /// <summary>
        /// Specialized evaluation for abilities used after jumping
        /// Includes bonus scoring for jump+ability combos
        /// </summary>
        private int EvaluateAbilityFromPositionForJump(Ability ability, int abilitySlot, Tile jumpDestination, List<Unit> targets, List<UnitAI> teammates)
        {
            int optionsCreated = 0;

            if (IsDirectionalAbility(ability))
            {
                Vector2Int[] directions = GetValidDirectionsForAbility(ability);
                foreach (var direction in directions)
                {
                    if (!WouldAbilityBeUseful(ability, jumpDestination, direction, null, targets, teammates))
                        continue;

                    var plan = new ActionPlan
                    {
                        isJump = true,
                        movementTarget = jumpDestination,
                        abilityToUse = ability,
                        abilitySlot = abilitySlot,
                        aimDirection = direction,
                        abilityFromPosition = jumpDestination
                    };
                    plan.CalculateScore(this, targets, teammates);
                    plan.totalScore += 20f; // High bonus for jump+ability combo
                    evaluatedActions.Add(plan);
                    optionsCreated++;
                }
            }
            else if (ability.targeting is SingleTargeting)
            {
                // Skip teleport abilities with jump - that's redundant
                if (HasTeleportEffect(ability)) return 0;

                foreach (var target in targets)
                {
                    if (!CanTargetUnit(target)) continue;
                    if (!IsTargetInRangeFromPosition(ability, jumpDestination, target.currentTile)) continue;

                    var plan = new ActionPlan
                    {
                        isJump = true,
                        movementTarget = jumpDestination,
                        abilityToUse = ability,
                        abilitySlot = abilitySlot,
                        targetTile = target.currentTile,
                        abilityFromPosition = jumpDestination
                    };
                    plan.CalculateScore(this, targets, teammates);
                    plan.totalScore += 20f;
                    evaluatedActions.Add(plan);
                    optionsCreated++;
                }
            }

            return optionsCreated;
        }

        /// <summary>
        /// Fallback evaluation for movement-only options when no abilities are viable
        /// </summary>
        private void EvaluateMovementOptions(List<Unit> targets, List<UnitAI> teammates)
        {
            var reachableTiles = GridManager.Instance.GetReachableTiles(unit.currentTile, unit.GetEffectiveMovementRange());

            int movementOptionsEvaluated = 0;
            foreach (var tile in reachableTiles)
            {
                if (tile.occupied && tile.currentUnit != unit) continue;

                var plan = new ActionPlan { movementTarget = tile };
                plan.CalculateScore(this, targets, teammates);
                evaluatedActions.Add(plan);
                movementOptionsEvaluated++;
            }

            if (enableDebugLogging)
            {
                Debug.Log($"[{unit.name}] Evaluated {movementOptionsEvaluated} movement-only options");
            }
        }

        #endregion

        #region Action Evaluation - Utility Methods

        /// <summary>
        /// Pre-validation to avoid creating ActionPlans for abilities that would accomplish nothing
        /// Tests ability effects against potential targets without actually executing
        /// </summary>
        private bool WouldAbilityBeUseful(Ability ability, Tile fromPosition, Vector2Int aimDirection, Tile targetTile, List<Unit> targets, List<UnitAI> teammates)
        {
            if (ability?.effects == null || ability.effects.Count == 0) return false;

            // Temporarily set position for accurate targeting simulation
            var originalTile = unit.currentTile;
            unit.currentTile = fromPosition;

            try
            {
                var ctx = new AbilityContext
                {
                    caster = unit,
                    ability = ability,
                    aimDir = aimDirection,
                    targetTile = targetTile
                };

                var potentialTargets = ability.targeting?.SelectTargets(ctx) ?? new List<Unit>();

                // Self-buffs are always useful
                if (HasSelfBuffEffect(ability)) return true;

                // Teleport effects need valid empty destinations
                if (HasTeleportEffect(ability))
                {
                    return targetTile != null &&
                           !targetTile.occupied &&
                           targetTile.passableTerrain &&
                           targetTile != fromPosition;
                }

                // Check if ability would hit valid targets for its effects
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

        /// <summary>
        /// Determines if a specific effect should target a specific unit
        /// Used to validate ability usefulness before creating ActionPlans
        /// </summary>
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
                    return false; // These don't target other units

                case MovementEffect movementEffect:                                             
                    return !movementEffect.applyToCaster && enemies.Contains(target);

                default:
                    return enemies.Contains(target); // Unknown effects assume enemy targeting
            }
        }

        /// <summary>
        /// Validates status effects against targets - debuffs for enemies, buffs for allies
        /// </summary>
        private bool IsStatusEffectValidForTarget(StatusEffect statusEffect, Unit target, List<Unit> enemies, List<UnitAI> teammates)
        {
            foreach (var statusApp in statusEffect.statusesToApply)
            {
                if (statusApp.applyTo != StatusEffect.ApplicationTarget.Targets &&
                    statusApp.applyTo != StatusEffect.ApplicationTarget.Both)
                    continue;

                bool isDebuff = IsDebuffEffect(statusApp.statusEffectData.effectType);

                if (isDebuff && enemies.Contains(target)) return true;
                if (!isDebuff && IsAlly(target)) return true;
            }

            return false;
        }

        /// <summary>
        /// Categorizes status effects as positive (buffs) or negative (debuffs)
        /// </summary>
        private bool IsDebuffEffect(StatusEffectType effectType)
        {
            switch (effectType)
            {
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
                    return true;

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
                    return true; // Unknown effects assumed to be debuffs
            }
        }

        private bool IsBuffEffect(StatusEffectType effectType) => !IsDebuffEffect(effectType);

        /// <summary>
        /// Gets valid directions for directional abilities based on targeting restrictions
        /// </summary>
        private Vector2Int[] GetValidDirectionsForAbility(Ability ability)
        {
            if (ability.targeting is LineTargeting lineTargeting && lineTargeting.horizontalOnly)
            {
                return new Vector2Int[] { Vector2Int.left, Vector2Int.right };
            }

            return new Vector2Int[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        }

        /// <summary>
        /// Difficulty-based search depth limiting to balance AI performance vs. computation time
        /// </summary>
        private int GetSearchDepthByDifficulty()
        {
            switch (difficulty)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return 3;
                case 4: return 4;
                case 5: return 5;
                case 6: return 7;
                case 7: return 9;
                case 8: return 12;
                case 9: return 15;
                case 10: return 20;
                default: return 5;
            }
        }

        private bool IsAlly(Unit testUnit)
        {
            if (testUnit is EnemyUnit enemyUnit)
            {
                var enemyAI = enemyUnit.GetComponent<UnitAI>();
                return enemyAI != null && enemyAI.teamId == teamId;
            }
            return false;
        }

        private List<Tile> GetValidTeleportTiles(Ability ability, Tile fromPosition)
        {
            var validTiles = new List<Tile>();
            var reachableTiles = GridManager.Instance.GetReachableTiles(fromPosition, ability.range);

            foreach (var tile in reachableTiles)
            {
                if (!tile.occupied && tile.passableTerrain && tile != fromPosition)
                    validTiles.Add(tile);
            }

            return validTiles;
        }

        private bool HasTeleportEffect(Ability ability) =>
            ability?.effects?.Any(effect => effect is TeleportEffect) ?? false;

        private bool HasSelfBuffEffect(Ability ability)
        {
            if (ability?.effects == null) return false;

            foreach (var effect in ability.effects)
            {
                if (effect is StatusEffect statusEffect)
                {
                    foreach (var statusApp in statusEffect.statusesToApply)
                    {
                        if ((statusApp.applyTo == StatusEffect.ApplicationTarget.Caster ||
                             statusApp.applyTo == StatusEffect.ApplicationTarget.Both) &&
                            IsBuffEffect(statusApp.statusEffectData.effectType))
                        {
                            return true;
                        }
                    }
                }
                // UPDATED: Check for self-targeting movement effects
                else if (effect is MovementEffect movementEffect && movementEffect.applyToCaster)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Range checking for abilities from different positions - temporarily changes unit position for accurate calculation
        /// </summary>
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

        private List<Tile> GetJumpableTiles()
        {
            var jumpableTiles = new List<Tile>();
            Tile startTile = unit.currentTile;
            if (startTile == null) return jumpableTiles;

            int jumpRange = 2; // Same as JumpSystem

            // Use IDENTICAL logic to JumpSystem.GetJumpableTiles()
            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile == null || tile == startTile) continue;

                int distance = GridManager.Instance.GetGridDistance(startTile, tile, true);

                // Only tiles at exactly jump range distance (2)
                if (distance == jumpRange && tile.passableTerrain && !tile.occupied)
                {
                    // Check for wall blocking before adding to jumpable tiles (same as JumpSystem)
                    if (!IsJumpBlockedByWalls(startTile, tile))
                    {
                        jumpableTiles.Add(tile);
                    }
                }
            }

            return jumpableTiles;
        }

        private bool IsDirectionalAbility(Ability ability) =>
            ability.targeting is LineTargeting ||
            ability.targeting is MovementLineTargeting ||
            (ability.targeting is AOETargeting && ability.rangeType == AbilityRangeType.Line);

        #endregion

        #region Action Selection

        /// <summary>
        /// Selects best action based on difficulty level and introduces appropriate randomness
        /// Higher difficulty = more optimal play, lower difficulty = more mistakes/suboptimal choices
        /// </summary>
        private ActionPlan SelectBestAction()
        {
            if (evaluatedActions.Count == 0) return null;

            // High difficulty: Always optimal
            if (difficulty >= 8)
            {
                LogSelectionReason("High difficulty - selecting optimal action");
                return evaluatedActions[0];
            }
            // Medium difficulty: Weighted selection from top options
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
                        LogSelectionReason($"Medium difficulty - selected option #{i + 1} with {weights[i] * 100:F0}% weight");
                        return evaluatedActions[i];
                    }
                }

                return evaluatedActions[0];
            }
            // Low difficulty: Random from top half
            else
            {
                int topHalf = Mathf.Max(1, evaluatedActions.Count / 2);
                int selectedIndex = Random.Range(0, topHalf);
                LogSelectionReason($"Low difficulty - randomly selected option #{selectedIndex + 1} from top {topHalf}");
                return evaluatedActions[selectedIndex];
            }
        }

        #endregion

        #region Action Execution

        /// <summary>
        /// Executes the selected action plan - handles movement and ability use in correct order
        /// </summary>
        private IEnumerator ExecuteActionPlan(ActionPlan plan)
        {
            LogActionExecution(plan);

            // Execute movement first (unless it's an ability-first plan)
            if (plan.movementTarget != null && plan.movementTarget != unit.currentTile && !plan.isAbilityFirst)
            {
                yield return StartCoroutine(ExecuteMovement(plan));
                yield return new WaitForSeconds(actionDelay);
            }

            // Execute ability
            if (plan.abilityToUse != null)
            {
                yield return StartCoroutine(ExecuteAbility(plan));
                yield return new WaitForSeconds(actionDelay);
            }

            // Execute post-ability movement for ability-first plans
            if (plan.isAbilityFirst && plan.movementTarget != null && plan.movementTarget != unit.currentTile)
            {
                yield return StartCoroutine(ExecuteMovement(plan));
            }

            LogActionComplete(plan);
        }

        /// <summary>
        /// Executes movement portion of action plan - handles both regular movement and jumping
        /// </summary>
        private IEnumerator ExecuteMovement(ActionPlan plan)
        {
            string movementType = plan.isJump ? "jumping" : "moving";

            if (enableDebugLogging)
            {
                Debug.Log($"[{unit.name}] {movementType} to {plan.movementTarget.name}");
            }

            var combatManager = FindObjectOfType<CombatManager>();
            if (combatManager == null)
            {
                Debug.LogError($"[{unit.name}] CombatManager not found - cannot execute movement");
                yield break;
            }

            if (plan.isJump)
            {
                if (jumpSystem != null)
                {
                    yield return StartCoroutine(ExecuteAIJump(plan.movementTarget));
                }
                else
                {
                    Debug.LogWarning($"[{unit.name}] JumpSystem not found - falling back to regular movement");
                    yield return StartCoroutine(ExecuteRegularMovement(combatManager, plan));
                }
            }
            else
            {
                yield return StartCoroutine(ExecuteRegularMovement(combatManager, plan));
            }
        }

        /// <summary>
        /// Executes regular pathfinding-based movement using CombatManager's animation system
        /// </summary>
        private IEnumerator ExecuteRegularMovement(CombatManager combatManager, ActionPlan plan)
        {
            var waypoints = GridManager.Instance.FindPathOptimized(
                unit.currentTile,
                plan.movementTarget,
                unit.GetEffectiveMovementRange()
            );

            if (waypoints.Count > 0)
            {
                yield return StartCoroutine(combatManager.ExecuteAnimatedMovement(
                    unit,
                    plan.movementTarget,
                    waypoints,
                    updatePlayerState: false
                ));
            }
        }

        /// <summary>
        /// Executes jump movement - validates jump possibility and uses JumpSystem animation
        /// </summary>
        private IEnumerator ExecuteAIJump(Tile destination)
        {
            if (!CanAIJumpToTile(destination))
            {
                Debug.LogError($"[{unit.name}] Invalid jump destination: {destination.name}");
                yield break;
            }

            // Update logical position before animation
            unit.SetCurrentTileLogical(destination);

            // Execute jump animation
            Vector3 startPos = unit.transform.position;
            Vector3 endPos = destination.transform.position;
            yield return StartCoroutine(jumpSystem.JumpAnimation(unit, startPos, endPos));
        }

        private bool CanAIJumpToTile(Tile targetTile)
        {
            if (targetTile == null) return false;

            int jumpRange = 2; // Same as JumpSystem

            int distance = GridManager.Instance.GetGridDistance(unit.currentTile, targetTile, true);
            if (distance != jumpRange) return false; // Must be exactly 2

            // Check occupancy and terrain (same as JumpSystem)
            if (targetTile.occupied || !targetTile.passableTerrain) return false;

            // Check for wall obstacles (same as JumpSystem)
            if (IsJumpBlockedByWalls(unit.currentTile, targetTile)) return false;

            return true;
        }

        private bool IsJumpBlockedByWalls(Tile startTile, Tile targetTile)
        {
            if (startTile == null || targetTile == null) return true;

            Vector3 startPos = startTile.transform.position;
            Vector3 endPos = targetTile.transform.position;

            // Calculate the actual jump distance to limit raycast
            float jumpDistance = Vector3.Distance(startPos, endPos);

            // Raycast slightly above ground level to avoid hitting floor colliders
            Vector3 rayStart = startPos + Vector3.up * 0.5f;
            Vector3 rayEnd = endPos + Vector3.up * 0.5f;
            Vector3 rayDirection = (rayEnd - rayStart).normalized;

            // FIXED: Use 90% of jump distance like JumpSystem does, not 50%
            float checkDistance = jumpDistance * 0.9f;

            // Check for walls on the "Walls" layer
            int wallsLayerMask = LayerMask.GetMask("Walls");

            if (Physics.Raycast(rayStart, rayDirection, out RaycastHit hit, checkDistance, wallsLayerMask))
            {
                if (enableDebugLogging)
                {
                    Debug.Log($"[{unit.name}] Jump blocked by wall: {hit.collider.name} at distance {hit.distance}");
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Executes ability portion of action plan with proper context and animation handling
        /// </summary>
        private IEnumerator ExecuteAbility(ActionPlan plan)
        {
            var ability = plan.abilityToUse;
            LogAbilityExecution(plan);

            // Create ability execution context
            var ctx = new AbilityContext
            {
                caster = unit,
                ability = ability,
                aimDir = plan.aimDirection,
                targetTile = plan.targetTile
            };

            // Execute ability
            bool success = plan.targetTile != null ?
                ability.ExecuteWithContext(ctx) :
                ability.Execute(unit, plan.aimDirection);

            if (success)
            {
                // NEW: Wait for complete ability sequence including camera transitions
                var combatManager = FindObjectOfType<CombatManager>();

                // Wait for camera transitions and effects to complete
                float maxWaitTime = 30f; // Safety timeout
                float elapsed = 0f;

                while (elapsed < maxWaitTime)
                {
                    // Check if camera is still transitioning or ability is still executing
                    bool cameraTransitioning = false;
                    var cameraController = FindObjectOfType<CameraController>();
                    if (cameraController != null)
                        cameraTransitioning = cameraController.IsTransitioning;

                    bool abilityExecuting = unit.currentAbilityContext != null;

                    if (!cameraTransitioning && !abilityExecuting)
                    {
                        break;
                    }

                    elapsed += Time.deltaTime;
                    yield return null;
                }

                LogAbilitySuccess(ability);
            }
            else
            {
                LogAbilityFailure(ability);
            }
        }

        #endregion

        #region Target Management

        private List<Unit> GetPotentialTargets()
        {
            var targets = new List<Unit>();

            foreach (var targetUnit in UnitManager.AllUnits)
            {
                if (targetUnit is EnemyUnit) continue; // Don't target other AI units by default
                if (!ShouldTargetUnit(targetUnit)) continue;
                targets.Add(targetUnit);
            }

            return targets;
        }

        /// <summary>
        /// Determines if a unit should be considered a valid target based on team rules and hostility settings
        /// </summary>
        private bool ShouldTargetUnit(Unit targetUnit)
        {
            // Check team membership for enemy units
            if (targetUnit is EnemyUnit enemyTarget)
            {
                var targetAI = enemyTarget.GetComponent<UnitAI>();
                if (targetAI != null && targetAI.teamId == teamId)
                    return false; // Same team
            }

            // Apply hostility rules
            return hostileToAllNonTeam ? true : targetUnit is PlayerUnit;
        }

        /// <summary>
        /// Checks if unit can be targeted considering temporary restrictions
        /// </summary>
        public bool CanTargetUnit(Unit target)
        {
            return unit.CanTarget(target) && !untargetableUnits.ContainsKey(target);
        }

        /// <summary>
        /// Adds a unit to the temporary untargetable list for specified duration
        /// Used for effects that make units briefly untargetable
        /// </summary>
        public void AddUntargetableUnit(Unit targetUnit, int duration)
        {
            if (targetUnit == null) return;

            if (untargetableUnits.ContainsKey(targetUnit))
                untargetableUnits[targetUnit] = Mathf.Max(untargetableUnits[targetUnit], duration);
            else
                untargetableUnits[targetUnit] = duration;
        }

        /// <summary>
        /// Marks a unit as high-priority target for specified duration
        /// Used for tactical marking or aggro effects
        /// </summary>
        public void AddTargetLikelyUnit(Unit targetUnit, int duration)
        {
            if (targetUnit == null) return;

            if (targetLikelyUnits.ContainsKey(targetUnit))
                targetLikelyUnits[targetUnit] = Mathf.Max(targetLikelyUnits[targetUnit], duration);
            else
                targetLikelyUnits[targetUnit] = duration;
        }

        public bool IsLikelyTarget(Unit targetUnit) =>
            targetUnit != null && targetLikelyUnits.ContainsKey(targetUnit);

        /// <summary>
        /// Updates targeting duration counters - called at end of each turn
        /// </summary>
        private void UpdateTargetingDurations()
        {
            UpdateDictionaryDurations(untargetableUnits);
            UpdateDictionaryDurations(targetLikelyUnits);
        }

        /// <summary>
        /// Helper method to decrement duration counters and remove expired entries
        /// </summary>
        private void UpdateDictionaryDurations(Dictionary<Unit, int> dictionary)
        {
            var unitsToRemove = new List<Unit>();
            var keys = new List<Unit>(dictionary.Keys);

            foreach (var targetUnit in keys)
            {
                if (targetUnit == null)
                {
                    unitsToRemove.Add(targetUnit);
                    continue;
                }

                dictionary[targetUnit]--;
                if (dictionary[targetUnit] <= 0)
                {
                    unitsToRemove.Add(targetUnit);
                }
            }

            foreach (var targetUnit in unitsToRemove)
            {
                dictionary.Remove(targetUnit);
            }
        }

        #endregion

        #region Debug Logging

        private void LogTurnStart()
        {
            if (!enableDebugLogging) return;

            Debug.Log($"=== [{unit.name}] TURN START ===");
            Debug.Log($"[{unit.name}] Position: {unit.currentTile?.name ?? "Unknown"}");
            Debug.Log($"[{unit.name}] Health: {unit.currentHealth}/{unit.characterData.maxHealth}");
            Debug.Log($"[{unit.name}] Personality: {personality}, Difficulty: {difficulty}");
        }

        private void LogEvaluationStart()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Starting action evaluation...");
        }

        private void LogTargetsAndTeammates(List<Unit> targets, List<UnitAI> teammates)
        {
            if (!enableDebugLogging) return;

            if (targets.Count > 0)
            {
                string targetNames = string.Join(", ", targets.Select(t => $"{t.name} (HP:{t.currentHealth})"));
                Debug.Log($"[{unit.name}] Found {targets.Count} targets: {targetNames}");
            }
            else
            {
                Debug.Log($"[{unit.name}] No valid targets found");
            }

            if (teammates.Count > 0)
            {
                string teammateNames = string.Join(", ", teammates.Select(t => t.name));
                Debug.Log($"[{unit.name}] Found {teammates.Count} teammates: {teammateNames}");
            }
        }

        private void LogEvaluationResults()
        {
            if (!enableDebugLogging) return;

            Debug.Log($"[{unit.name}] Evaluation complete. Generated {evaluatedActions.Count} total action options");

            if (evaluatedActions.Count > 0)
            {
                Debug.Log($"[{unit.name}] TOP {Mathf.Min(topActionsToLog, evaluatedActions.Count)} ACTION OPTIONS:");

                for (int i = 0; i < Mathf.Min(topActionsToLog, evaluatedActions.Count); i++)
                {
                    var action = evaluatedActions[i];
                    string actionDescription = GetActionDescription(action);
                    string scoreBreakdown = GetScoreBreakdown(action);

                    Debug.Log($"[{unit.name}] #{i + 1}: {actionDescription} | Total: {action.totalScore:F1}{scoreBreakdown}");
                }
            }
        }

        private void LogSelectedAction()
        {
            if (!enableDebugLogging) return;

            if (selectedPlan != null)
            {
                string actionDescription = GetActionDescription(selectedPlan);
                Debug.Log($"[{unit.name}] SELECTED ACTION: {actionDescription} (Score: {selectedPlan.totalScore:F1})");
            }
        }

        private void LogSelectionReason(string reason)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Selection logic: {reason}");
        }

        private void LogNoValidActions()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] No valid actions available - ending turn");
        }

        private void LogActionExecution(ActionPlan plan)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] === EXECUTING ACTION ===");
        }

        private void LogAbilityExecution(ActionPlan plan)
        {
            if (!enableDebugLogging) return;

            string targetInfo = "";
            if (plan.targetTile != null && plan.targetTile.currentUnit != null)
            {
                targetInfo = $" targeting {plan.targetTile.currentUnit.name}";
            }
            else if (plan.aimDirection != Vector2Int.zero)
            {
                targetInfo = $" aimed {GetDirectionName(plan.aimDirection)}";
            }

            Debug.Log($"[{unit.name}] Using ability '{plan.abilityToUse.abilityName}'{targetInfo}");
        }

        private void LogAbilitySuccess(Ability ability)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Ability '{ability.abilityName}' executed successfully");
        }

        private void LogAbilityFailure(Ability ability)
        {
            if (!enableDebugLogging) return;
            Debug.LogWarning($"[{unit.name}] Ability '{ability.abilityName}' failed to execute");
        }

        private void LogActionComplete(ActionPlan plan)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Action execution complete");
        }

        private void LogTurnEnd()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] === TURN END ===");
        }

        /// <summary>
        /// Creates human-readable description of an action plan for debugging
        /// Handles both ability-first and movement-first action sequences
        /// </summary>
        private string GetActionDescription(ActionPlan action)
        {
            var parts = new List<string>();

            if (action.isAbilityFirst)
            {
                // Ability executed first, then movement
                if (action.abilityToUse != null)
                {
                    string abilityDesc = $"Use '{action.abilityToUse.abilityName}'";
                    if (action.targetTile != null && action.targetTile.currentUnit != null)
                    {
                        abilityDesc += $" on {action.targetTile.currentUnit.name}";
                    }
                    else if (action.aimDirection != Vector2Int.zero)
                    {
                        abilityDesc += $" {GetDirectionName(action.aimDirection)}";
                    }
                    parts.Add(abilityDesc);
                }

                if (action.movementTarget != null && action.movementTarget != action.abilityFromPosition)
                {
                    string movementType = action.isJump ? "Jump" : "Move";
                    parts.Add($"then {movementType} to {action.movementTarget.name}");
                }
            }
            else
            {
                // Movement first, then ability
                if (action.movementTarget != null && action.movementTarget != unit.currentTile)
                {
                    string movementType = action.isJump ? "Jump" : "Move";
                    parts.Add($"{movementType} to {action.movementTarget.name}");
                }
                else if (action.movementTarget == null || action.movementTarget == unit.currentTile)
                {
                    parts.Add("Stay in place");
                }

                if (action.abilityToUse != null)
                {
                    string abilityDesc = $"Use '{action.abilityToUse.abilityName}'";
                    if (action.targetTile != null && action.targetTile.currentUnit != null)
                    {
                        abilityDesc += $" on {action.targetTile.currentUnit.name}";
                    }
                    else if (action.aimDirection != Vector2Int.zero)
                    {
                        abilityDesc += $" {GetDirectionName(action.aimDirection)}";
                    }
                    parts.Add(abilityDesc);
                }
            }

            return string.Join(" + ", parts);
        }

        /// <summary>
        /// Creates detailed score breakdown for debugging when enabled
        /// Shows individual scoring components that contributed to total action score
        /// </summary>
        private string GetScoreBreakdown(ActionPlan action)
        {
            if (!logDetailedScoring) return "";

            var breakdown = new List<string>();

            if (action.damageScore != 0) breakdown.Add($"Dmg:{action.damageScore:F1}");
            if (action.positionScore != 0) breakdown.Add($"Pos:{action.positionScore:F1}");
            if (action.safetyScore != 0) breakdown.Add($"Saf:{action.safetyScore:F1}");
            if (action.teamworkScore != 0) breakdown.Add($"Team:{action.teamworkScore:F1}");
            if (action.statusEffectScore != 0) breakdown.Add($"Eff:{action.statusEffectScore:F1}");

            return breakdown.Count > 0 ? $" ({string.Join(", ", breakdown)})" : "";
        }

        /// <summary>
        /// Converts Vector2Int directions to readable compass directions for logging
        /// </summary>
        private string GetDirectionName(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return "North";
            if (direction == Vector2Int.down) return "South";
            if (direction == Vector2Int.left) return "West";
            if (direction == Vector2Int.right) return "East";
            return "Unknown";
        }

        #endregion

        #region Public Properties

        public int Difficulty => difficulty;
        public AIPersonality Personality => personality;
        public int TeamId => teamId;
        public bool HostileToAllNonTeam => hostileToAllNonTeam;

        #endregion
    }
}