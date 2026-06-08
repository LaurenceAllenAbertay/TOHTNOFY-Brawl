using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Sits on the unit's GameObject and acts as the MonoBehaviour bridge for the
    /// PassiveAbility ScriptableObject. Calls Initialise on Start and Cleanup on
    /// OnDestroy, and exposes the owning Unit for the passive to reference.
    ///
    /// Add this component to any unit prefab that should have a passive.
    /// For PlayerUnits the passive is read from UnitLoadoutManager (keyed by CharacterData).
    /// For EnemyUnits the passive is read from the EnemyLoadout component on the same GameObject.
    /// </summary>
    public class PassiveAbilityHandler : MonoBehaviour
    {
        // Owning unit — set in Awake so the passive can always safely reference it.
        public Unit Owner { get; private set; }

        // Routes through UnitLoadoutManager so both PlayerUnit and EnemyUnit are handled
        // transparently — no call site needs to know which path was taken.
        public PassiveAbility Passive => UnitLoadoutManager.GetPassive(Owner);

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