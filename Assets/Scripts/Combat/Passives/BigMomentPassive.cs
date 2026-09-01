using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Big Moment")]
    public class BigMomentPassive : PassiveAbility
    {
        [Header("Attack Buff")]
        public StatusEffectData attackUpData;

        public float attackPower = 3f;

        public int buffDuration = 3;

        [Header("Health Restore")]
        [Range(0f, 1f)] public float restoreHealthFraction = 0.5f;

        private readonly HashSet<Unit> _activeOwners = new HashSet<Unit>();
        private readonly HashSet<Unit> _triggeredOwners = new HashSet<Unit>();
        private bool _subscribed;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            _activeOwners.Add(handler.Owner);

            if (!_subscribed)
            {
                _subscribed = true;
                Unit.OnPreReceiveDamage += InterceptDamage;
            }
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            _activeOwners.Remove(handler.Owner);
            _triggeredOwners.Remove(handler.Owner);

            if (_activeOwners.Count == 0 && _subscribed)
            {
                _subscribed = false;
                Unit.OnPreReceiveDamage -= InterceptDamage;
            }

            base.Cleanup(handler);
        }

        private int InterceptDamage(Unit victim, Unit attacker, Ability sourceAbility, int amount)
        {
            if (!_activeOwners.Contains(victim)) return amount;
            if (_triggeredOwners.Contains(victim)) return amount;
            if (victim.currentHealth - amount > 0) return amount;

            _triggeredOwners.Add(victim);

            if (BigMomentSequencer.Instance == null)
            {
                return 0;
            }

            Debug.Log($"[BigMoment] {victim.name} survived a lethal hit! Queuing Big Moment sequence.");

            BigMomentSequencer.Instance.Enqueue(
                victim, attackUpData, attackPower, buffDuration, restoreHealthFraction);

            return 0;
        }
    }
}