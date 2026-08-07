using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Unit")]
    public class CharacterData : ScriptableObject
    {
        [Header("Info")]
        public string characterName;
        public Sprite portrait;
        
        public AssetReferenceGameObject prefab;

        public AssetReferenceGameObject overworldPrefab;

        [Header("Stats")]
        public int maxHealth;
        public int attack;
        public int defense;
        public int speed;

        [Header("Abilities")]
        public Ability[] availableAbilities = new Ability[0];

        [Header("Passive")]
        public PassiveAbility[] availablePassives = new PassiveAbility[0];

        [Header("Default Loadout")]
        public Ability[] defaultAbilities = new Ability[3];

        [Header("UI")]
        public Color uiColour = Color.white;

        [Header("Dialogue")]
        public CharacterDialogueData dialogueData;
    }
}