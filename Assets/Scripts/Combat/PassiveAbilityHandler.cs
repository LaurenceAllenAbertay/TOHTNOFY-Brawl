using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Sits on the unit's GameObject and acts as the MonoBehaviour bridge for the
    /// PassiveAbility ScriptableObject. Calls Initialise on Start and Cleanup on
    /// OnDestroy, and exposes the owning Unit for the passive to reference.
    ///
    /// Add this component to any unit prefab that has a passive slot in CharacterData.
    /// The passive SO is read directly from characterData.passive — no extra assignment needed.
    /// </summary>
    public class PassiveAbilityHandler : MonoBehaviour
    {
        // Owning unit — set in Awake so the passive can always safely reference it.
        public Unit Owner { get; private set; }

        // Convenience accessor so passives don't need to cast Owner themselves.
        public PassiveAbility Passive => Owner?.characterData?.passive;

        private void Awake()
        {
            Owner = GetComponent<Unit>();
        }

        private void Start()
        {
            if (Passive != null)
                Passive.Initialise(this);
        }

        private void OnDestroy()
        {
            if (Passive != null)
                Passive.Cleanup(this);
        }
    }
}