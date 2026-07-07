using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class EnemyLoadout : MonoBehaviour
    {
        [Header("Equipped Abilities")]
        public Ability[] abilityLoadout = new Ability[3];

        [Header("Passive")]
        public PassiveAbility passive;
    }
}