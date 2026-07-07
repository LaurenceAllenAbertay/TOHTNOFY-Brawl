using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Staticy")]
    public class StaticyPassive : PassiveAbility
    {
        [Header("Retaliation")]
        public int retaliationDamage = 5;

        private Unit _owner;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            _owner = handler.Owner;

            System.Func<Unit, Unit, Ability, int, int> onPreDamage = OnPreDamage;
            Unit.OnPreReceiveDamage += onPreDamage;
            RegisterCleanup(() => Unit.OnPreReceiveDamage -= onPreDamage);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            _owner = null;
            base.Cleanup(handler);
        }

        private int OnPreDamage(Unit victim, Unit attacker, Ability sourceAbility, int amount)
        {
            if (victim != _owner) return amount;
            
            if (attacker == null || attacker.IsDead) return amount;
            
            if (sourceAbility == null || sourceAbility.attackType != AttackType.Melee) return amount;
            
            Debug.Log($"[Staticy] {victim.name} retaliates against {attacker.name} for {retaliationDamage} damage.");
            attacker.currentHealth -= retaliationDamage;

            Unit.NotifyHealthChanged(attacker);

            if (attacker.currentHealth <= 0 && !attacker.IsDead)
            {
                attacker.Die(victim);
            }
            else
            {
                var attackerAnimator = attacker.GetComponent<UnitAnimator>();
                attackerAnimator?.PlayHurt();
            }
            
            return amount;
        }
    }
}