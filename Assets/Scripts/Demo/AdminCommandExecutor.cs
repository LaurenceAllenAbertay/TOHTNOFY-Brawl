using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace DDD.TNFY.BRAWL
{
    public static class AdminCommandExecutor
    {
        public static IEnumerator ExecuteSpawn(AdminParsedCommand command, Tile targetTile, System.Action<string> onComplete)
        {
            if (AdminCommandRegistry.Instance == null)
            {
                onComplete?.Invoke("Error: AdminCommandRegistry not present in scene.");
                yield break;
            }

            string characterName = command.positionalArg;
            if (string.IsNullOrEmpty(characterName))
            {
                onComplete?.Invoke("Syntax error: /spawn requires a character name.");
                yield break;
            }

            var characterData = AdminCommandRegistry.Instance.ResolveCharacter(characterName);
            if (characterData == null)
            {
                onComplete?.Invoke($"Error: no spawnable character named '{characterName}'.");
                yield break;
            }

            if (characterData.prefab == null || !characterData.prefab.RuntimeKeyIsValid())
            {
                onComplete?.Invoke($"Error: '{characterName}' has no prefab assigned on its CharacterData.");
                yield break;
            }

            if (targetTile == null)
            {
                onComplete?.Invoke("Error: no target tile.");
                yield break;
            }

            if (targetTile.occupied)
            {
                onComplete?.Invoke("Error: target tile is occupied.");
                yield break;
            }

            if (!targetTile.passableTerrain)
            {
                onComplete?.Invoke("Error: target tile is not passable.");
                yield break;
            }

            var overrides = ParseSpawnOverrides(command, out string overrideError);
            if (overrideError != null)
            {
                onComplete?.Invoke(overrideError);
                yield break;
            }

            int? team = null;
            if (command.HasKey("team"))
            {
                string teamRaw = command.GetSingleValue("team");
                if (!int.TryParse(teamRaw, out int teamInt))
                {
                    onComplete?.Invoke($"Syntax error: 'team' must be a whole number, got '{teamRaw}'.");
                    yield break;
                }
                if (teamInt < 0)
                {
                    onComplete?.Invoke("Syntax error: 'team' must be >= 0.");
                    yield break;
                }
                team = teamInt;
            }

            bool? forceAI = null;
            if (command.HasKey("ai"))
            {
                string aiRaw = command.GetSingleValue("ai");
                if (!bool.TryParse(aiRaw, out bool aiBool))
                {
                    onComplete?.Invoke($"Syntax error: 'ai' must be true or false, got '{aiRaw}'.");
                    yield break;
                }
                forceAI = aiBool;
            }

            var abilityNames = command.GetValues("abilities");
            var passiveName = command.GetSingleValue("passive");

            if (abilityNames.Count > 3)
            {
                onComplete?.Invoke("Error: /spawn supports at most 3 abilities.");
                yield break;
            }

            Ability[] resolvedAbilities = null;
            if (abilityNames.Count > 0)
            {
                resolvedAbilities = new Ability[3];
                for (int i = 0; i < abilityNames.Count && i < 3; i++)
                {
                    var ability = AdminCommandRegistry.ResolveAbilityOnCharacter(characterData, abilityNames[i]);
                    if (ability == null)
                    {
                        onComplete?.Invoke($"Error: '{abilityNames[i]}' is not in {characterData.characterName}'s available abilities.");
                        yield break;
                    }
                    resolvedAbilities[i] = ability;
                }
            }

            PassiveAbility resolvedPassive = null;
            if (!string.IsNullOrEmpty(passiveName))
            {
                resolvedPassive = AdminCommandRegistry.ResolvePassiveOnCharacter(characterData, passiveName);
                if (resolvedPassive == null)
                {
                    onComplete?.Invoke($"Error: '{passiveName}' is not in {characterData.characterName}'s available passives.");
                    yield break;
                }
            }

            if (UnitLoadoutManager.Instance == null)
            {
                onComplete?.Invoke("Error: UnitLoadoutManager not present in scene.");
                yield break;
            }

            bool hasExplicitLoadout = abilityNames.Count > 0 || !string.IsNullOrEmpty(passiveName);
            if (hasExplicitLoadout)
                UnitLoadoutManager.Instance.SetLoadout(characterData, resolvedAbilities, resolvedPassive);

            AsyncOperationHandle<GameObject> handle = characterData.prefab.InstantiateAsync();
            yield return handle;

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                onComplete?.Invoke($"Error: failed to load prefab for '{characterName}'.");
                yield break;
            }

            var go = handle.Result;
            go.transform.position = targetTile.transform.position;
            go.name = characterData.characterName;

            var unit = go.GetComponent<Unit>();
            if (unit == null)
            {
                Addressables.ReleaseInstance(go);
                onComplete?.Invoke($"Error: prefab for '{characterName}' has no Unit component.");
                yield break;
            }

            unit.ApplyCharacterData(characterData);

            if (team.HasValue)
                unit.team = team.Value;

            unit.SetCurrentTile(targetTile);

            ApplySpawnOverrides(unit, overrides);

            string aiMessage = ApplyAIOverride(unit, forceAI);

            bool addedToTurnOrder = TurnManager.Instance != null && TurnManager.Instance.AddUnitToTurnOrder(unit);

            string result = $"Spawned '{characterData.characterName}' on tile {targetTile.name}" +
                             (team.HasValue ? $", team {team.Value}" : "") +
                             (aiMessage != null ? $", {aiMessage}" : "") + ".";

            if (!addedToTurnOrder && (unit is PlayerUnit || unit is NpcUnit))
                result += " Warning: unit was not added to the turn order (already present or TurnManager missing).";

            onComplete?.Invoke(result);
        }

        private static string ApplyAIOverride(Unit unit, bool? forceAI)
        {
            if (!forceAI.HasValue) return null;

            var existingAI = unit.GetComponent<UnitAI>();

            if (forceAI.Value)
            {
                if (existingAI != null)
                    return "AI-controlled";

                if (unit.GetComponent<AIExecutor>() == null)
                    unit.gameObject.AddComponent<AIExecutor>();

                unit.gameObject.AddComponent<UnitAI>();
                return "AI-controlled";
            }
            else
            {
                if (existingAI != null)
                    Object.Destroy(existingAI);

                var existingExecutor = unit.GetComponent<AIExecutor>();
                if (existingExecutor != null)
                    Object.Destroy(existingExecutor);

                return "player-controlled";
            }
        }

        private class SpawnOverrides
        {
            public int? currentHealth;
            public int? maxHealth;
            public int? speed;
            public int? attack;
            public int? defense;
            public int? jumpRange;
        }

        private static SpawnOverrides ParseSpawnOverrides(AdminParsedCommand command, out string error)
        {
            error = null;
            var result = new SpawnOverrides();

            if (!TryParseOptionalInt(command, "max_health", out result.maxHealth, out error)) return null;
            if (!TryParseOptionalInt(command, "current_health", out result.currentHealth, out error)) return null;
            if (!TryParseOptionalInt(command, "speed", out result.speed, out error)) return null;
            if (!TryParseOptionalInt(command, "attack", out result.attack, out error)) return null;
            if (!TryParseOptionalInt(command, "defense", out result.defense, out error)) return null;
            if (!TryParseOptionalInt(command, "jump_range", out result.jumpRange, out error)) return null;

            return result;
        }

        private static bool TryParseOptionalInt(AdminParsedCommand command, string key, out int? value, out string error)
        {
            value = null;
            error = null;

            if (!command.HasKey(key)) return true;

            string raw = command.GetSingleValue(key);
            if (!int.TryParse(raw, out int parsed))
            {
                error = $"Syntax error: '{key}' must be a whole number, got '{raw}'.";
                return false;
            }

            value = parsed;
            return true;
        }

        private static void ApplySpawnOverrides(Unit unit, SpawnOverrides overrides)
        {
            if (overrides.maxHealth.HasValue)
            {
                unit.maxHealth = overrides.maxHealth.Value;
                unit.currentHealth = overrides.maxHealth.Value;
            }

            if (overrides.currentHealth.HasValue)
                unit.currentHealth = overrides.currentHealth.Value;

            if (overrides.speed.HasValue)
                unit.currentSpeed = overrides.speed.Value;

            if (overrides.jumpRange.HasValue)
                unit.JumpRange = overrides.jumpRange.Value;

            unit.AdminOverrideBaseStats(overrides.attack, overrides.defense);
        }

        public static string ExecuteTeleport(Unit activeUnit, Tile targetTile)
        {
            if (activeUnit == null)
                return "Error: no active unit.";

            if (targetTile == null)
                return null;

            if (targetTile.occupied || !targetTile.passableTerrain)
                return null;

            activeUnit.SetCurrentTile(targetTile);
            return $"Teleported {activeUnit.name} to tile {targetTile.name}.";
        }

        public static string ExecuteReset()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            SceneManager.LoadScene(sceneName);
            return $"Reset combat — reloading '{sceneName}' from the beginning.";
        }

        public static IEnumerator ExecuteKill(Unit targetUnit, System.Action<string> onComplete)
        {
            if (targetUnit == null)
            {
                onComplete?.Invoke("Error: no target unit.");
                yield break;
            }

            if (targetUnit.IsDead || targetUnit.IsBody)
            {
                onComplete?.Invoke($"Error: '{targetUnit.name}' is already dead or is a body.");
                yield break;
            }

            string killedName = targetUnit.name;

            targetUnit.Die();

            Unit returnToUnit = TurnManager.Instance != null ? TurnManager.Instance.CurrentUnit : null;

            if (UnitDownedSequencer.Instance != null)
                yield return UnitDownedSequencer.Instance.StartCoroutine(
                    UnitDownedSequencer.Instance.DrainDownedQueue(returnToUnit));

            onComplete?.Invoke($"Killed '{killedName}'.");
        }

        public static string ExecuteDelete(Unit targetUnit)
        {
            if (targetUnit == null)
                return "Error: no target unit.";

            if (!targetUnit.gameObject.activeSelf)
                return $"Error: '{targetUnit.name}' has already been deleted.";

            string deletedName = targetUnit.name;

            targetUnit.AdminDelete();

            return $"Deleted '{deletedName}' — removed instantly, no body left behind.";
        }

        public static string ExecuteClear(AdminParsedCommand command)
        {
            string targetRaw = command.ResolvePositional("target");
            if (string.IsNullOrEmpty(targetRaw))
                targetRaw = "all";

            System.Func<Unit, bool> matchesTarget;
            switch (targetRaw.ToLowerInvariant())
            {
                case "player":
                    matchesTarget = u => u.team == 0;
                    break;
                case "enemy":
                    matchesTarget = u => u.team != 0;
                    break;
                case "all":
                    matchesTarget = u => true;
                    break;
                default:
                    return $"Syntax error: 'target' must be player, enemy, or all — got '{targetRaw}'.";
            }

            var toDelete = new List<Unit>();
            foreach (var unit in UnitManager.AllUnits)
            {
                if (unit == null) continue;
                if (!unit.gameObject.activeSelf) continue;
                if (matchesTarget(unit))
                    toDelete.Add(unit);
            }

            if (toDelete.Count == 0)
                return $"No units matched target:{targetRaw.ToLowerInvariant()} — nothing to clear.";

            int count = toDelete.Count;
            foreach (var unit in toDelete)
                unit.AdminDelete();

            return $"Cleared {count} unit(s) — target:{targetRaw.ToLowerInvariant()}.";
        }

        public static string ExecuteHeal(AdminParsedCommand command, Unit targetUnit)
        {
            if (targetUnit == null)
                return "Error: no target unit.";

            string amountRaw = command.ResolvePositional("amount");
            if (string.IsNullOrEmpty(amountRaw))
                return "Syntax error: /heal requires an amount.";

            if (!int.TryParse(amountRaw, out int amount))
                return $"Syntax error: '{amountRaw}' is not a whole number.";

            if (amount < 0)
                return "Syntax error: /heal does not accept negative amounts.";

            if (targetUnit.IsDead || targetUnit.IsBody)
                return $"Error: '{targetUnit.name}' is dead or is a body and cannot be healed.";

            int maxHealth = targetUnit.maxHealth > 0 ? targetUnit.maxHealth : targetUnit.currentHealth;

            int before = targetUnit.currentHealth;
            targetUnit.currentHealth = Mathf.Min(targetUnit.currentHealth + amount, maxHealth);
            int actualHeal = targetUnit.currentHealth - before;

            Unit.NotifyHealthChanged(targetUnit);

            return $"Healed '{targetUnit.name}' for {actualHeal} ({before} -> {targetUnit.currentHealth} / {maxHealth}).";
        }

        public static string ExecuteDamage(AdminParsedCommand command, Unit targetUnit)
        {
            if (targetUnit == null)
                return "Error: no target unit.";

            string amountRaw = command.ResolvePositional("amount");
            if (string.IsNullOrEmpty(amountRaw))
                return "Syntax error: /damage requires an amount.";

            if (!int.TryParse(amountRaw, out int amount))
                return $"Syntax error: '{amountRaw}' is not a whole number.";

            if (amount < 0)
                return "Syntax error: /damage does not accept negative amounts.";

            if (targetUnit.IsDead || targetUnit.IsBody)
                return $"Error: '{targetUnit.name}' is already dead or is a body.";

            int before = targetUnit.currentHealth;
            targetUnit.ReceiveDamage(amount);

            if (targetUnit.IsDead)
                return $"Dealt {amount} damage to '{targetUnit.name}' — defeated.";

            int actualDamage = before - targetUnit.currentHealth;
            if (actualDamage <= 0)
                return $"'{targetUnit.name}' took no damage (mitigated).";

            return $"Dealt {actualDamage} damage to '{targetUnit.name}' ({before} -> {targetUnit.currentHealth}).";
        }

        public static string ExecuteTrueDamage(AdminParsedCommand command, Unit targetUnit)
        {
            if (targetUnit == null)
                return "Error: no target unit.";

            string amountRaw = command.ResolvePositional("amount");
            if (string.IsNullOrEmpty(amountRaw))
                return "Syntax error: /truedamage requires an amount.";

            if (!int.TryParse(amountRaw, out int amount))
                return $"Syntax error: '{amountRaw}' is not a whole number.";

            if (amount < 0)
                return "Syntax error: /truedamage does not accept negative amounts.";

            if (targetUnit.IsDead || targetUnit.IsBody)
                return $"Error: '{targetUnit.name}' is already dead or is a body.";

            int before = targetUnit.currentHealth;
            targetUnit.AdminApplyTrueDamage(amount);

            if (targetUnit.IsDead)
                return $"Dealt {amount} true damage to '{targetUnit.name}' — defeated.";

            int actualDamage = before - targetUnit.currentHealth;
            return $"Dealt {actualDamage} true damage to '{targetUnit.name}' ({before} -> {targetUnit.currentHealth}).";
        }

        public static string ExecuteStatusEffect(AdminParsedCommand command, Unit targetUnit)
        {
            if (AdminCommandRegistry.Instance == null)
                return "Error: AdminCommandRegistry not present in scene.";

            if (StatusEffectManager.Instance == null)
                return "Error: StatusEffectManager not present in scene.";

            if (targetUnit == null)
                return "Error: no target unit.";

            string effectName = command.ResolvePositional("name");
            if (string.IsNullOrEmpty(effectName))
                return "Syntax error: /statuseffect requires a status effect name.";

            var effectData = AdminCommandRegistry.Instance.ResolveStatusEffect(effectName);
            if (effectData == null)
                return $"Error: no status effect named '{effectName}'.";

            int duration = 2;
            if (command.HasKey("duration"))
            {
                string durationRaw = command.GetSingleValue("duration");
                if (!int.TryParse(durationRaw, out duration))
                    return $"Syntax error: 'Duration' must be a whole number, got '{durationRaw}'.";
            }

            float power = 0f;
            bool hasPower = command.HasKey("power");
            if (hasPower)
            {
                string powerRaw = command.GetSingleValue("power");
                if (!int.TryParse(powerRaw, out int powerInt))
                    return $"Syntax error: 'Power' must be a whole number, got '{powerRaw}'.";

                power = powerInt;
            }

            if (targetUnit.IsDead || targetUnit.IsBody)
                return $"Error: '{targetUnit.name}' is dead or is a body and cannot receive status effects.";

            var instance = StatusEffectManager.Instance.ApplyStatusEffect(
                targetUnit, effectData, source: null, duration: duration, power: power);

            if (instance == null)
                return $"'{effectData.effectName}' failed to apply to '{targetUnit.name}'.";

            bool showsStacks = effectData.stackingBehavior == StatusEffectData.StackingBehavior.AddStacks;

            return $"Applied '{effectData.effectName}' to '{targetUnit.name}' for {duration} turns" +
                   (hasPower && showsStacks ? $" ({instance.stackCount} stacks)."
                    : hasPower ? $" (power {power})."
                    : ".");
        }

        public static IEnumerator ExecuteSkip(Unit targetUnit, System.Action<string> onComplete)
        {
            if (TurnManager.Instance == null)
            {
                onComplete?.Invoke("Error: TurnManager not present in scene.");
                yield break;
            }

            if (targetUnit == null)
            {
                onComplete?.Invoke("Error: no target unit.");
                yield break;
            }

            if (!TurnManager.Instance.TurnOrder.Contains(targetUnit))
            {
                onComplete?.Invoke($"Error: '{targetUnit.name}' is not in the current turn order.");
                yield break;
            }

            if (targetUnit == TurnManager.Instance.CurrentUnit)
            {
                onComplete?.Invoke($"'{targetUnit.name}' is already the active unit.");
                yield break;
            }

            string skippedFrom = TurnManager.Instance.CurrentUnit?.name ?? "unknown";

            yield return TurnManager.Instance.EndTurn();

            bool success = TurnManager.Instance.AdminForceSkipToUnit(targetUnit);

            onComplete?.Invoke(success
                ? $"Skipped from '{skippedFrom}' to '{targetUnit.name}'."
                : $"Error: '{targetUnit.name}' left the turn order before the skip completed.");
        }
    }
}