using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Big Moment — Kallper's passive.
    ///
    /// Once per combat: if an incoming hit would reduce Kallper's health to zero or below,
    /// the damage is fully absorbed and he survives on 1 HP. Immediately after, his attack
    /// is buffed for 3 turns via an AttackUp status effect.
    ///
    /// The one-per-combat limit is tracked by a bool on this ScriptableObject instance.
    /// Because Unity reuses SO instances across play sessions in the editor, the flag is
    /// reset in both Initialise (combat start) and Cleanup (combat end) so it never
    /// carries over between runs.
    ///
    /// Setup: equip this SO through UnitLoadoutManager for players, or EnemyLoadout for
    /// enemies. Assign attackUpData to the project's AttackUp StatusEffectData asset.
    /// Set attackPower and buffDuration in the Inspector.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Passives > Big Moment
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Big Moment")]
    public class BigMomentPassive : PassiveAbility
    {
        [Header("Attack Buff")]
        [Tooltip("AttackUp StatusEffectData asset applied after absorbing a lethal hit.")]
        public StatusEffectData attackUpData;

        [Tooltip("Flat attack bonus granted by the buff.")]
        public float attackPower = 3f;

        [Tooltip("How many turns the attack buff lasts.")]
        public int buffDuration = 3;

        // Instance flag — reset on Initialise so it never persists between combat sessions.
        private bool _triggered;

        // Cached owner reference so the event handler doesn't close over `handler`.
        private Unit _owner;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            _triggered = false;
            _owner = handler.Owner;

            System.Func<Unit, Unit, Ability, int, int> onPreDamage = InterceptDamage;
            Unit.OnPreReceiveDamage += onPreDamage;
            RegisterCleanup(() => Unit.OnPreReceiveDamage -= onPreDamage);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            _triggered = false;
            _owner = null;
            base.Cleanup(handler);
        }

        private int InterceptDamage(Unit victim, Unit attacker, Ability sourceAbility, int amount)
        {
            // Only intercept damage aimed at our owner, only once, and only when lethal.
            if (_triggered) return amount;
            if (victim != _owner) return amount;
            if (_owner.currentHealth - amount > 0) return amount;

            _triggered = true;
            Debug.Log($"[BigMoment] {_owner.name} survived a lethal hit! Absorbing {amount} damage.");

            // Leave Kallper on exactly 1 HP.
            _owner.currentHealth = 1;

            // Apply the attack buff.
            if (attackUpData != null && StatusEffectManager.Instance != null)
            {
                StatusEffectManager.Instance.ApplyStatusEffect(
                    _owner, attackUpData, _owner, buffDuration, attackPower);
                Debug.Log($"[BigMoment] {_owner.name} gains +{attackPower} attack for {buffDuration} turns.");
            }

            // Return 0 so ReceiveDamage applies no further damage after we've set HP manually.
            return 0;
        }
    }
}
