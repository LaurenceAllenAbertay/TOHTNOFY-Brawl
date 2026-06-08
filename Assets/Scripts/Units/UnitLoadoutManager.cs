using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Single runtime source of truth for what abilities and passive each unit has equipped.
    ///
    /// ── Player units ─────────────────────────────────────────────────────────
    /// Loadouts are keyed by CharacterData and persist across scenes and combats via
    /// DontDestroyOnLoad. The lobby writes to this manager; all in-combat systems read from it.
    ///
    /// ── Enemy units ──────────────────────────────────────────────────────────
    /// Loadouts are read from the EnemyLoadout component on the unit's own GameObject.
    /// They are NOT stored in this manager — enemies do not persist between scenes.
    ///
    /// ── How to access a unit's loadout ───────────────────────────────────────
    /// Use the static helpers rather than calling the Dictionary directly:
    ///
    ///     Ability[] abilities = UnitLoadoutManager.GetAbilities(unit);
    ///     PassiveAbility passive = UnitLoadoutManager.GetPassive(unit);
    ///
    /// Both methods work for PlayerUnit and EnemyUnit transparently.
    /// Batch 2 updates all call sites (AbilityPanelController, TurnManager, AIEvaluator, etc.)
    /// to use these helpers.
    /// </summary>
    public class UnitLoadoutManager : MonoBehaviour
    {
        public static UnitLoadoutManager Instance { get; private set; }

        // ── Internal storage ──────────────────────────────────────────────────

        // Player loadouts keyed by CharacterData asset reference.
        // Using CharacterData as the key means the lobby can write before units are
        // spawned and the spawner can read after — no unit instance required.
        private readonly Dictionary<CharacterData, UnitLoadout> _playerLoadouts
            = new Dictionary<CharacterData, UnitLoadout>();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ── Player loadout API ────────────────────────────────────────────────

        /// <summary>
        /// Writes (or overwrites) the full loadout for a player character.
        /// Called by the lobby when the player confirms their choices.
        /// </summary>
        public void SetPlayerLoadout(CharacterData characterData, Ability[] abilities, PassiveAbility passive)
        {
            if (characterData == null)
            {
                Debug.LogWarning("[UnitLoadoutManager] SetPlayerLoadout called with null CharacterData.");
                return;
            }

            _playerLoadouts[characterData] = new UnitLoadout
            {
                abilities = SanitiseAbilityArray(abilities),
                passive   = passive
            };
        }

        /// <summary>
        /// Returns true if a player loadout exists for this CharacterData.
        /// Useful for the spawner to confirm the manager was populated before spawning.
        /// </summary>
        public bool HasPlayerLoadout(CharacterData characterData)
            => characterData != null && _playerLoadouts.ContainsKey(characterData);

        /// <summary>
        /// Removes all stored player loadouts.
        /// Call this when starting a completely fresh save/session.
        /// </summary>
        public void ClearAllPlayerLoadouts()
            => _playerLoadouts.Clear();

        // ── Static unit-agnostic helpers (the main public API) ────────────────

        /// <summary>
        /// Returns the equipped ability array for any unit.
        /// • PlayerUnit → looks up the manager's dictionary by characterData.
        /// • EnemyUnit  → reads from the EnemyLoadout component on the unit's GameObject.
        /// Returns an empty array (never null) if no loadout is found.
        /// </summary>
        public static Ability[] GetAbilities(Unit unit)
        {
            if (unit == null) return new Ability[3];

            if (unit is PlayerUnit)
                return GetPlayerAbilities(unit.characterData);

            if (unit is EnemyUnit)
            {
                var enemyLoadout = unit.GetComponent<EnemyLoadout>();
                if (enemyLoadout != null)
                    return SanitiseAbilityArray(enemyLoadout.abilityLoadout);

                Debug.LogWarning($"[UnitLoadoutManager] EnemyUnit '{unit.name}' has no EnemyLoadout component.");
                return new Ability[3];
            }

            return new Ability[3];
        }

        /// <summary>
        /// Returns the equipped passive for any unit.
        /// • PlayerUnit → looks up the manager's dictionary by characterData.
        /// • EnemyUnit  → reads from the EnemyLoadout component on the unit's GameObject.
        /// Returns null if no passive is set.
        /// </summary>
        public static PassiveAbility GetPassive(Unit unit)
        {
            if (unit == null) return null;

            if (unit is PlayerUnit)
                return GetPlayerPassive(unit.characterData);

            if (unit is EnemyUnit)
            {
                var enemyLoadout = unit.GetComponent<EnemyLoadout>();
                if (enemyLoadout != null)
                    return enemyLoadout.passive;

                Debug.LogWarning($"[UnitLoadoutManager] EnemyUnit '{unit.name}' has no EnemyLoadout component.");
                return null;
            }

            return null;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static Ability[] GetPlayerAbilities(CharacterData characterData)
        {
            if (characterData == null) return new Ability[3];

            if (Instance == null)
            {
                Debug.LogWarning("[UnitLoadoutManager] Instance is null — manager not in scene.");
                return new Ability[3];
            }

            if (Instance._playerLoadouts.TryGetValue(characterData, out var loadout))
                return SanitiseAbilityArray(loadout.abilities);

            Debug.LogWarning($"[UnitLoadoutManager] No player loadout found for '{characterData.characterName}'. " +
                             $"Was SetPlayerLoadout called before combat started?");
            return new Ability[3];
        }

        private static PassiveAbility GetPlayerPassive(CharacterData characterData)
        {
            if (characterData == null) return null;

            if (Instance == null)
            {
                Debug.LogWarning("[UnitLoadoutManager] Instance is null — manager not in scene.");
                return null;
            }

            if (Instance._playerLoadouts.TryGetValue(characterData, out var loadout))
                return loadout.passive;

            return null;
        }

        /// <summary>
        /// Ensures the returned array is always length 3 and never null.
        /// Shields call sites from malformed data without crashing.
        /// </summary>
        private static Ability[] SanitiseAbilityArray(Ability[] source)
        {
            var result = new Ability[3];
            if (source == null) return result;
            for (int i = 0; i < 3 && i < source.Length; i++)
                result[i] = source[i];
            return result;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Plain data container for one unit's equipped loadout.
    /// Stored inside UnitLoadoutManager's dictionary for player units.
    /// </summary>
    [System.Serializable]
    public class UnitLoadout
    {
        /// <summary>Always length 3. Null slots are legal (empty ability slot).</summary>
        public Ability[] abilities = new Ability[3];

        /// <summary>The equipped passive. Null means no passive.</summary>
        public PassiveAbility passive;
    }
}