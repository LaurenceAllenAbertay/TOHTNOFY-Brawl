using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Unit")]
    public class CharacterData : ScriptableObject
    {
        [Header("Info")]
        public string characterName;
        public Sprite portrait;
        
        public GameObject prefab;

        public GameObject overworldPrefab;

        [Header("Stats")]
        public int maxHealth;
        public int attack;
        public int defense;
        public int speed;

        [Header("Abilities")]
        public Ability[] availableAbilities = new Ability[0];

        [Header("Passive")]
        public PassiveAbility[] availablePassives = new PassiveAbility[0];

        [Header("UI")]
        public Color uiColour = Color.white;

        [Header("Dialogue")]
        public CharacterDialogueData dialogueData;
    }
}