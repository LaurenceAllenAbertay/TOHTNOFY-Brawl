using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class NeutralUnit : Unit
    {
        [Header("Neutral Unit Abilities")]
        public Ability environmentAbility;
        
        public Ability destructionAbility;

        [Header("Spawn Source")]
        public SpawnNeutralUnitEffect spawnSource;
        
        public override bool IsNeutral => true;
        
        public override void ReceiveDamage(int amount, Unit attacker = null, Ability sourceAbility = null)
        {
            int healthBefore = currentHealth;
            base.ReceiveDamage(amount, attacker, sourceAbility);

            if (IsDead && healthBefore > 0 && destructionAbility != null)
            {
                var ctx = new AbilityContext
                {
                    caster = this,
                    ability = destructionAbility,
                    aimDir  = currentFacing
                };
                StartCoroutine(ExecuteAbilityCoroutine(ctx));
            }
        }

        protected virtual void OnDestroy()
        {
            spawnSource?.OnInstanceDestroyed(this);
        }
    }
}