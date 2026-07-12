using System.Collections;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public static class AdminCommandExecutor
    {
        public static string ExecuteSpawn(AdminParsedCommand command, Tile targetTile)
        {
            if (AdminCommandRegistry.Instance == null)
                return "Error: AdminCommandRegistry not present in scene.";

            string characterName = command.positionalArg;
            if (string.IsNullOrEmpty(characterName))
                return "Syntax error: /spawn requires a character name.";

            var characterData = AdminCommandRegistry.Instance.ResolveCharacter(characterName);
            if (characterData == null)
                return $"Error: no spawnable character named '{characterName}'.";

            if (characterData.prefab == null)
                return $"Error: '{characterName}' has no prefab assigned on its CharacterData.";

            if (targetTile == null)
                return "Error: no target tile.";

            if (targetTile.occupied)
                return "Error: target tile is occupied.";

            if (!targetTile.passableTerrain)
                return "Error: target tile is not passable.";

            var overrides = ParseSpawnOverrides(command, out string overrideError);
            if (overrideError != null)
                return overrideError;

            var abilityNames = command.GetValues("abilities");
            var passiveName = command.GetSingleValue("passive");

            Ability[] resolvedAbilities = new Ability[3];
            for (int i = 0; i < abilityNames.Count && i < 3; i++)
            {
                var ability = AdminCommandRegistry.ResolveAbilityOnCharacter(characterData, abilityNames[i]);
                if (ability == null)
                    return $"Error: '{abilityNames[i]}' is not in {characterData.characterName}'s available abilities.";
                resolvedAbilities[i] = ability;
            }
            if (abilityNames.Count > 3)
                return "Error: /spawn supports at most 3 abilities.";

            PassiveAbility resolvedPassive = null;
            if (!string.IsNullOrEmpty(passiveName))
            {
                resolvedPassive = AdminCommandRegistry.ResolvePassiveOnCharacter(characterData, passiveName);
                if (resolvedPassive == null)
                    return $"Error: '{passiveName}' is not in {characterData.characterName}'s available passives.";
            }

            if (UnitLoadoutManager.Instance == null)
                return "Error: UnitLoadoutManager not present in scene.";

            UnitLoadoutManager.Instance.SetPlayerLoadout(characterData, resolvedAbilities, resolvedPassive);

            var prefab = characterData.prefab;
            bool wasPrefabActive = prefab.activeSelf;
            prefab.SetActive(false);
            var go = Object.Instantiate(prefab, targetTile.transform.position, prefab.transform.rotation);
            prefab.SetActive(wasPrefabActive);

            go.name = characterData.characterName;

            var unit = go.GetComponent<Unit>();
            if (unit == null)
            {
                Object.Destroy(go);
                return $"Error: prefab for '{characterName}' has no Unit component.";
            }

            unit.characterData = characterData;
            unit.SetCurrentTile(targetTile);
            go.SetActive(true);

            ApplySpawnOverrides(unit, overrides);

            return $"Spawned '{characterData.characterName}' on tile {targetTile.name}.";
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
            if (command.HasKey("power"))
            {
                if (effectData.stackingBehavior != StatusEffectData.StackingBehavior.AddStacks)
                    return $"Syntax error: '{effectData.effectName}' does not use Power " +
                           $"(stacking behavior is {effectData.stackingBehavior}, not AddStacks).";

                string powerRaw = command.GetSingleValue("power");
                if (!int.TryParse(powerRaw, out int powerInt))
                    return $"Syntax error: 'Power' must be a whole number, got '{powerRaw}'.";

                if (powerInt < 1)
                    return "Syntax error: 'Power' must be at least 1.";

                power = powerInt;
            }

            if (targetUnit.IsDead || targetUnit.IsBody)
                return $"Error: '{targetUnit.name}' is dead or is a body and cannot receive status effects.";

            var instance = StatusEffectManager.Instance.ApplyStatusEffect(
                targetUnit, effectData, source: null, duration: duration, power: power);

            if (instance == null)
                return $"'{effectData.effectName}' failed to apply to '{targetUnit.name}'.";

            return $"Applied '{effectData.effectName}' to '{targetUnit.name}' for {duration} turns" +
                   (command.HasKey("power") ? $" ({instance.stackCount} stacks)." : ".");
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