using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class PassiveAbilityHandler : MonoBehaviour
    {
        public Unit Owner { get; private set; }

        private PassiveAbility _initialisedPassive;

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
            var passive = Passive;
            if (passive == _initialisedPassive) return;

            CleanupCurrentPassive();

            if (passive == null) return;

            _initialisedPassive = passive;
            passive.Initialise(this);
        }

        public void CleanupCurrentPassive()
        {
            if (_initialisedPassive == null) return;

            _initialisedPassive.Cleanup(this);
            _initialisedPassive = null;
        }

        private void OnDestroy()
        {
            CleanupCurrentPassive();
        }
    }
}