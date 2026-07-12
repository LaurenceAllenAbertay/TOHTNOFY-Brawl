using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class AdminCommandRegistry : MonoBehaviour
    {
        public static AdminCommandRegistry Instance { get; private set; }

        [Header("Spawnable Characters")]
        [SerializeField] private CharacterData[] spawnableCharacters;

        [Header("Status Effects")]
        [SerializeField] private StatusEffectData[] statusEffects;

        private Dictionary<string, CharacterData> _characterLookup;
        private Dictionary<string, StatusEffectData> _statusEffectLookup;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            BuildLookups();
        }

        private void BuildLookups()
        {
            _characterLookup = new Dictionary<string, CharacterData>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var character in spawnableCharacters)
            {
                if (character == null) continue;

                if (_characterLookup.ContainsKey(character.characterName))
                {
                    Debug.LogWarning(
                        $"[AdminCommandRegistry] Duplicate CharacterData name '{character.characterName}' — " +
                        $"the admin console will resolve this name to whichever asset was registered first. " +
                        $"Rename one of the assets to avoid ambiguity.");
                    continue;
                }

                _characterLookup[character.characterName] = character;
            }

            _statusEffectLookup = new Dictionary<string, StatusEffectData>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var effect in statusEffects)
            {
                if (effect == null) continue;

                if (_statusEffectLookup.ContainsKey(effect.effectName))
                {
                    Debug.LogWarning(
                        $"[AdminCommandRegistry] Duplicate StatusEffectData name '{effect.effectName}' — " +
                        $"the admin console will resolve this name to whichever asset was registered first. " +
                        $"Rename one of the assets to avoid ambiguity.");
                    continue;
                }

                _statusEffectLookup[effect.effectName] = effect;
            }
        }

        private static string NormaliseToken(string token) =>
            string.IsNullOrEmpty(token) ? token : token.Replace('_', ' ');

        public CharacterData ResolveCharacter(string typedName)
        {
            if (string.IsNullOrEmpty(typedName)) return null;
            _characterLookup.TryGetValue(NormaliseToken(typedName), out var result);
            return result;
        }

        public StatusEffectData ResolveStatusEffect(string typedName)
        {
            if (string.IsNullOrEmpty(typedName)) return null;
            _statusEffectLookup.TryGetValue(NormaliseToken(typedName), out var result);
            return result;
        }

        public static Ability ResolveAbilityOnCharacter(CharacterData character, string typedName)
        {
            if (character == null || string.IsNullOrEmpty(typedName)) return null;
            string normalised = NormaliseToken(typedName);

            return character.availableAbilities?
                .FirstOrDefault(a => a != null &&
                    string.Equals(a.abilityName, normalised, System.StringComparison.OrdinalIgnoreCase));
        }

        public static PassiveAbility ResolvePassiveOnCharacter(CharacterData character, string typedName)
        {
            if (character == null || string.IsNullOrEmpty(typedName)) return null;
            string normalised = NormaliseToken(typedName);

            return character.availablePassives?
                .FirstOrDefault(p => p != null &&
                    string.Equals(p.passiveName, normalised, System.StringComparison.OrdinalIgnoreCase));
        }
    }
}