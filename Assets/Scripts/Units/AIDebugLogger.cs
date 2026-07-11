using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class AIDebugLogger
    {
        private readonly Unit unit;
        private readonly bool enableDebugLogging;
        private readonly bool logDetailedScoring;
        private readonly int  topActionsToLog;

        public AIDebugLogger(Unit unit, bool enableDebugLogging, bool logDetailedScoring = false, int topActionsToLog = 3)
        {
            this.unit               = unit;
            this.enableDebugLogging = enableDebugLogging;
            this.logDetailedScoring = logDetailedScoring;
            this.topActionsToLog    = topActionsToLog;
        }

        public void LogTurnStart()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"=== [{unit.name}] TURN START ===");
            Debug.Log($"[{unit.name}] Position: {unit.currentTile?.name ?? "Unknown"}");
            Debug.Log($"[{unit.name}] Health: {unit.currentHealth}/{unit.characterData.maxHealth}");
        }

        public void LogTurnEnd()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] === TURN END ===");
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
                Debug.Log($"[{unit.name}] Teammates: {names}");
            }
        }

        public void LogSelectedAction(ActionPlan plan)
        {
            if (!enableDebugLogging) return;

            if (plan == null)
            {
                Debug.Log($"[{unit.name}] No plan selected.");
                return;
            }

            Debug.Log($"[{unit.name}] SELECTED PLAN: {GetActionDescription(plan, unit)}" +
                      $"{(string.IsNullOrEmpty(plan.debugReason) ? "" : $" | Reason: {plan.debugReason}")}");
        }

        public void LogSelectionReason(string reason)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Planner: {reason}");
        }

        public void LogNoValidActions()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] No valid actions available — ending turn");
        }

        public void LogActionExecution()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] === EXECUTING ACTION ===");
        }

        public void LogMovement(string movementType, Tile destination)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] {movementType} to {destination?.name ?? "null"}");
        }

        public void LogAbilityExecution(ActionPlan plan)
        {
            if (!enableDebugLogging || plan?.abilityToUse == null) return;

            string targetInfo = "";
            if (plan.targetTile?.currentUnit != null)
                targetInfo = $" targeting {plan.targetTile.currentUnit.name}";
            else if (plan.aimDirection != Vector2Int.zero)
                targetInfo = $" aimed {GetDirectionName(plan.aimDirection)}";

            Debug.Log($"[{unit.name}] Using '{plan.abilityToUse.abilityName}'{targetInfo}");
        }

        public void LogAbilitySuccess(Ability ability)
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] '{ability.abilityName}' executed successfully");
        }

        public void LogAbilityFailure(Ability ability)
        {
            if (!enableDebugLogging) return;
            Debug.LogWarning($"[{unit.name}] '{ability.abilityName}' failed to execute");
        }

        public void LogActionComplete()
        {
            if (!enableDebugLogging) return;
            Debug.Log($"[{unit.name}] Action execution complete");
        }

        public static string GetActionDescription(ActionPlan plan, Unit unit)
        {
            if (plan == null) return "None";

            var parts = new List<string>();

            if (plan.isAbilityFirst)
            {
                if (plan.abilityToUse != null)
                    parts.Add(FormatAbilityPart(plan));

                if (plan.movementTarget != null && plan.movementTarget != unit.currentTile)
                    parts.Add($"then {(plan.isJump ? "Jump" : "Move")} to {plan.movementTarget.name}");
            }
            else
            {
                if (plan.movementTarget != null && plan.movementTarget != unit.currentTile)
                    parts.Add($"{(plan.isJump ? "Jump" : "Move")} to {plan.movementTarget.name}");
                else
                    parts.Add("Stay in place");

                if (plan.abilityToUse != null)
                    parts.Add(FormatAbilityPart(plan));
            }

            return parts.Count > 0 ? string.Join(" + ", parts) : "No action";
        }

        private static string FormatAbilityPart(ActionPlan plan)
        {
            string desc = $"Use '{plan.abilityToUse.abilityName}'";
            if (plan.targetTile?.currentUnit != null)
                desc += $" on {plan.targetTile.currentUnit.name}";
            else if (plan.aimDirection != Vector2Int.zero)
                desc += $" {GetDirectionName(plan.aimDirection)}";
            return desc;
        }

        public static string GetDirectionName(Vector2Int direction)
        {
            if (direction == Vector2Int.up)    return "North";
            if (direction == Vector2Int.down)  return "South";
            if (direction == Vector2Int.left)  return "West";
            if (direction == Vector2Int.right) return "East";
            return "Unknown";
        }
    }
}