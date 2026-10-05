using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Unit")]
    public class CharacterData : DialogueSpeakerData
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

        [Header("Movement")]
        public AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Jump")]
        public AnimationCurve jumpTravelCurve = new AnimationCurve();
        public AnimationCurve jumpArcCurve = new AnimationCurve();

        [Header("UI")]
        public Color uiColour = Color.white;

        [Header("Dialogue")]
        public CharacterDialogueData dialogueData;

        public override string DisplayName => characterName;

        public override Sprite SpeakerPortrait => portrait;

        public override Color SpeakerColour =>
            dialogueData != null ? dialogueData.characterColour : Color.white;
    }
}