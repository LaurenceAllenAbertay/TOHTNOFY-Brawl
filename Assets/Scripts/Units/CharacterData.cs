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

        [Header("Stats")]
        public int maxHealth;
        public int attack;
        public int defense;
        public int speed;

        [Header("Abilities")]
        public Ability[] abilityLoadout = new Ability[3];

        [Header("Passive")]
        public PassiveAbility passive;

        [Header("Dialogue")]
        [Tooltip("Per-character dialogue lines and trigger entries. " +
                 "Create via Assets > Create > TNFY Brawl > Dialogue > Character Dialogue Data.")]
        public CharacterDialogueData dialogueData;
    }

}