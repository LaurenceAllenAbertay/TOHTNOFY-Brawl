using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitLoadoutManager : MonoBehaviour
    {
        public static UnitLoadoutManager Instance { get; private set; }

        private readonly Dictionary<CharacterData, UnitLoadout> _loadouts
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

        public void SetLoadout(CharacterData characterData, Ability[] abilities, PassiveAbility passive)
        {
            if (characterData == null)
            {
                Debug.LogWarning("[UnitLoadoutManager] SetLoadout called with null CharacterData.");
                return;
            }

            if (!_loadouts.TryGetValue(characterData, out var loadout))
            {
                loadout = new UnitLoadout();
                _loadouts[characterData] = loadout;
            }

            if (abilities != null)
                loadout.abilities = SanitiseAbilityArray(abilities);

            loadout.passive = passive;
        }

        public bool HasLoadout(CharacterData characterData)
            => characterData != null && _loadouts.ContainsKey(characterData);

        public void ClearAllLoadouts()
            => _loadouts.Clear();

        public static Ability[] GetAbilities(Unit unit)
        {
            if (unit == null || unit.characterData == null) return new Ability[3];

            if (Instance != null && Instance._loadouts.TryGetValue(unit.characterData, out var loadout)
                                  && loadout.abilities != null)
                return SanitiseAbilityArray(loadout.abilities);

            return SanitiseAbilityArray(unit.characterData.defaultAbilities);
        }

        public static PassiveAbility GetPassive(Unit unit)
        {
            if (unit == null || unit.characterData == null) return null;

            if (Instance != null && Instance._loadouts.TryGetValue(unit.characterData, out var loadout))
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
        public Ability[] abilities;

        public PassiveAbility passive;
    }
}