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

        [Tooltip("The PlayerUnit prefab for this character. " +
                 "Used by DebugMapSpawner to spawn the correct prefab per character.")]
        public GameObject prefab;

        [Header("Stats")]
        public int maxHealth;
        public int attack;
        public int defense;
        public int speed;

        [Header("Abilities")]
        [Tooltip("The full pool of abilities this character can have equipped. " +
                 "Used by the lobby to populate ability slot dropdowns. " +
                 "Which 3 are actually equipped at runtime is stored in UnitLoadoutManager (for players) " +
                 "or EnemyLoadout (for enemies).")]
        public Ability[] availableAbilities = new Ability[0];

        [Header("Passive")]
        [Tooltip("The passives this character can have equipped. " +
                 "Used by the lobby to populate the passive dropdown — only these options " +
                 "are shown, enforcing per-character passive pools. " +
                 "Which one is actually equipped at runtime is stored in UnitLoadoutManager (for players) " +
                 "or EnemyLoadout (for enemies).")]
        public PassiveAbility[] availablePassives = new PassiveAbility[0];

        [Header("UI")]
        [Tooltip("Colour used to tint UI elements associated with this character, " +
                 "e.g. the End Turn button background. " +
                 "Separate from the dialogue bubble colour on CharacterDialogueData.")]
        public Color uiColour = Color.white;

        [Header("Dialogue")]
        [Tooltip("Per-character dialogue lines and trigger entries. " +
                 "Create via Assets > Create > TNFY Brawl > Dialogue > Character Dialogue Data.")]
        public CharacterDialogueData dialogueData;
    }
}