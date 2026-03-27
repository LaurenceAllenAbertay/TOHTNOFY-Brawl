using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Plain class that owns all AI debug logging.
    /// Instantiated by UnitAI and passed to AIEvaluator and AIExecutor.
    /// Contains no Unity lifecycle methods — not a MonoBehaviour.
    /// </summary>
    public class AIDebugLogger
    {
        private readonly Unit unit;
        private readonly bool enableDebugLogging;
        private readonly bool logDetailedScoring;
        private readonly int topActionsToLog;

        public AIDebugLogger(Unit unit, bool enableDebugLogging, bool logDetailedScoring = false, int topActionsToLog = 3)
        {
            this.unit = unit;
            this.enableDebugLogging = enableDebugLogging;
            this.logDetailedScoring = logDetailedScoring;
            this.topActionsToLog = topActionsToLog;
        }

        public void LogTurnStart()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"=== [{unit.name}] TURN START ===");
            Debug.Log($"[{unit.name}] Position: {unit.currentTile?.name ?? "Unknown"}");
            Debug.Log($"[{unit.name}] Health: {unit.currentHealth}/{unit.characterData.maxHealth}");
        }

        public void LogEvaluationStart()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Starting action evaluation...");
        }

        public void LogTargetsAndTeammates(List<Unit> targets, List<UnitAI> teammates)
        {
            if (!enableDebugLogging) return;

            if (targets.Count > 0)
            {
                string names = string.Join(", ", targets.Select(t => $"{t.name} (HP:{t.currentHealth})"));
                Debug.Log($"[{unit.name}] Found {targets.Count} targets: {names}");
            }
            else
            {
                Debug.Log($"[{unit.name}] No valid targets found");
            }

            if (teammates.Count > 0)
            {
                string names = string.Join(", ", teammates.Select(t => t.name));
                Debug.Log($"[{unit.name}] Found {teammates.Count} teammates: {names}");
            }
        }

        public void LogEvaluationResults(List<ActionPlan> evaluatedActions)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Evaluation complete. Generated {evaluatedActions.Count} total action options");

            if (evaluatedActions.Count > 0)
            {
                Debug.Log($"[{unit.name}] TOP {Mathf.Min(topActionsToLog, evaluatedActions.Count)} ACTION OPTIONS:");
                for (int i = 0; i < Mathf.Min(topActionsToLog, evaluatedActions.Count); i++)
                {
                    var action = evaluatedActions[i];
                    Debug.Log($"[{unit.name}] #{i + 1}: {GetActionDescription(action, unit)} | Total: {action.totalScore:F1}{GetScoreBreakdown(action)}");
                }
            }
        }

        public void LogSelectedAction(ActionPlan selectedPlan)
        {
            if (!enableDebugLogging || selectedPlan == null) return;
            Debug.Log($"[{unit.name}] SELECTED ACTION: {GetActionDescription(selectedPlan, unit)} (Score: {selectedPlan.totalScore:F1})");
        }

        public void LogSelectionReason(string reason)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Selection logic: {reason}");
        }

        public void LogNoValidActions()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] No valid actions available - ending turn");
        }

        public void LogActionExecution()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] === EXECUTING ACTION ===");
        }

        public void LogAbilityExecution(ActionPlan plan)
        {
            if (!enableDebugLogging) return;
            string targetInfo = "";
            if (plan.targetTile != null && plan.targetTile.currentUnit != null)
                targetInfo = $" targeting {plan.targetTile.currentUnit.name}";
            else if (plan.aimDirection != Vector2Int.zero)
                targetInfo = $" aimed {GetDirectionName(plan.aimDirection)}";
            Debug.Log($"[{unit.name}] Using ability '{plan.abilityToUse.abilityName}'{targetInfo}");
        }

        public void LogAbilitySuccess(Ability ability)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Ability '{ability.abilityName}' executed successfully");
        }

        public void LogAbilityFailure(Ability ability)
        {
            if (!enableDebugLogging) return;
            Debug.LogWarning($"[{unit.name}] Ability '{ability.abilityName}' failed to execute");
        }

        public void LogActionComplete()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Action execution complete");
        }

        public void LogTurnEnd()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] === TURN END ===");
        }

        public void LogMovement(string movementType, Tile destination)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] {movementType} to {destination?.name}");
        }

        // ── Static helpers ───────────────────────────────────────────────────────

        public static string GetActionDescription(ActionPlan action, Unit unit)
        {
            var parts = new List<string>();

            if (action.isAbilityFirst)
            {
                if (action.abilityToUse != null)
                {
                    string desc = $"Use '{action.abilityToUse.abilityName}'";
                    if (action.targetTile?.currentUnit != null) desc += $" on {action.targetTile.currentUnit.name}";
                    else if (action.aimDirection != Vector2Int.zero) desc += $" {GetDirectionName(action.aimDirection)}";
                    parts.Add(desc);
                }
                if (action.movementTarget != null && action.movementTarget != action.abilityFromPosition)
                    parts.Add($"then {(action.isJump ? "Jump" : "Move")} to {action.movementTarget.name}");
            }
            else
            {
                if (action.movementTarget != null && action.movementTarget != unit.currentTile)
                    parts.Add($"{(action.isJump ? "Jump" : "Move")} to {action.movementTarget.name}");
                else
                    parts.Add("Stay in place");

                if (action.abilityToUse != null)
                {
                    string desc = $"Use '{action.abilityToUse.abilityName}'";
                    if (action.targetTile?.currentUnit != null) desc += $" on {action.targetTile.currentUnit.name}";
                    else if (action.aimDirection != Vector2Int.zero) desc += $" {GetDirectionName(action.aimDirection)}";
                    parts.Add(desc);
                }
            }

            return string.Join(" + ", parts);
        }

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

        public static string GetDirectionName(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return "North";
            if (direction == Vector2Int.down) return "South";
            if (direction == Vector2Int.left) return "West";
            if (direction == Vector2Int.right) return "East";
            return "Unknown";
        }
    }
}