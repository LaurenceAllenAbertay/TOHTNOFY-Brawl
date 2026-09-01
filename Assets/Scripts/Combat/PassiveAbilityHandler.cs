using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class PassiveAbilityHandler : MonoBehaviour
    {
        public Unit Owner { get; private set; }
        
        private bool _initialised;
        
        public PassiveAbility Passive => UnitLoadoutManager.GetPassive(Owner);

        private void Awake()
        {
            Owner = GetComponent<Unit>();
        }

        private void Start()
        {
            TryInitialisePassive();
        }

        public void TryInitialisePassive()
        {
            if (_initialised) return;
            if (Passive == null) return;

            _initialised = true;
            Passive.Initialise(this);
        }

        private void OnDestroy()
        {
            if (Passive != null)
                Passive.Cleanup(this);
        }
    }
}