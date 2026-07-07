using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class PassiveAbilityHandler : MonoBehaviour
    {
        public Unit Owner { get; private set; }
        
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