using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Staticy — Kallper's passive.
    ///
    /// When Kallper is hit by an ability marked as Melee (via the AttackType dropdown on
    /// the Ability SO), the attacker immediately takes flat retaliatory damage.
    /// The retaliation bypasses the attacker's defences — it is a direct HP deduction,
    /// not a call to ReceiveDamage — so it cannot be shielded, guarded, or dodged.
    ///
    /// Designers control which abilities count as melee by setting AttackType = Melee on
    /// the Ability asset. This correctly includes high-range melee-looking moves and
    /// excludes close-range ranged attacks.
    ///
    /// Setup: equip this SO through UnitLoadoutManager for players, or EnemyLoadout for
    /// enemies. Set retaliationDamage in the Inspector.
    ///
    /// Create via: Assets > Create > TNFY Brawl > Passives > Staticy
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Staticy")]
    public class StaticyPassive : PassiveAbility
    {
        [Header("Retaliation")]
        [Tooltip("Flat damage dealt back to the attacker on a melee hit. " +
                 "Bypasses attacker's defence, shields, and guards.")]
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
            // Only react when Kallper is the one being hit.
            if (victim != _owner) return amount;

            // No attacker means environmental damage (tile effects, recoil) — not melee.
            if (attacker == null || attacker.IsDead) return amount;

            // Only trigger on abilities explicitly marked as Melee.
            // Null sourceAbility means non-ability damage — not melee.
            if (sourceAbility == null || sourceAbility.attackType != AttackType.Melee) return amount;

            // Deal retaliation directly to avoid re-triggering OnPreReceiveDamage
            // (which would loop if the attacker also had a retaliatory passive).
            Debug.Log($"[Staticy] {victim.name} retaliates against {attacker.name} for {retaliationDamage} damage.");
            attacker.currentHealth -= retaliationDamage;

            if (attacker.currentHealth <= 0 && !attacker.IsDead)
                attacker.Die(victim);

            // Return the original amount — Staticy does not reduce incoming damage.
            return amount;
        }
    }
}
