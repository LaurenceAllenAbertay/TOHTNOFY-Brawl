using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitLoadoutManager : MonoBehaviour
    {
        public static UnitLoadoutManager Instance { get; private set; }

        private readonly Dictionary<CharacterData, UnitLoadout> _playerLoadouts
            = new Dictionary<CharacterData, UnitLoadout>();

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

        public bool HasPlayerLoadout(CharacterData characterData)
            => characterData != null && _playerLoadouts.ContainsKey(characterData);

        public void ClearAllPlayerLoadouts()
            => _playerLoadouts.Clear();

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

        private static Ability[] SanitiseAbilityArray(Ability[] source)
        {
            var result = new Ability[3];
            if (source == null) return result;
            for (int i = 0; i < 3 && i < source.Length; i++)
                result[i] = source[i];
            return result;
        }
    }

    [System.Serializable]
    public class UnitLoadout
    {
        public Ability[] abilities = new Ability[3];

        public PassiveAbility passive;
    }
}