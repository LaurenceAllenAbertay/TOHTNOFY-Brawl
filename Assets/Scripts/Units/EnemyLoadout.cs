using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Holds the runtime ability loadout and passive for a specific enemy unit instance.
    /// Attach this to every enemy prefab and fill in the slots in the Inspector.
    ///
    /// Because this lives on the prefab rather than on CharacterData, two enemy instances
    /// that share the same CharacterData (e.g. two Kallper enemies) can have completely
    /// different ability kits — set independently by designers per-prefab.
    ///
    /// UnitLoadoutManager reads from this component via Unit.GetLoadout() for enemy units,
    /// keeping all ability access behind a single consistent API regardless of unit type.
    /// </summary>
    public class EnemyLoadout : MonoBehaviour
    {
        [Header("Equipped Abilities")]
        [Tooltip("The three abilities this enemy has equipped. Null slots are allowed.")]
        public Ability[] abilityLoadout = new Ability[3];

        [Header("Passive")]
        [Tooltip("The passive ability this enemy has. Can be null.")]
        public PassiveAbility passive;
    }
}