using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// A Unit that exists as a neutral map object — neither player nor enemy.
    /// NeutralUnits are never placed in the turn order, are invisible to AI targeting,
    /// and participate only in the environment turn that runs at the end of every round.
    ///
    /// Each NeutralUnit has two optional ability slots:
    ///   environmentAbility  — fired once per environment turn (end of every round).
    ///   destructionAbility  — fired automatically when this unit's health reaches zero,
    ///                         immediately before the down sequence begins.
    ///
    /// This pattern generalises to any future neutral: a ticking bomb uses environmentAbility
    /// to count down and destructionAbility to explode; a healing mug uses environmentAbility
    /// to pulse healing and destructionAbility for a big burst.
    ///
    /// Wire up the two ability slots and the owning SpawnNeutralUnitEffect in the Inspector
    /// on the prefab. UnitManager holds NeutralUnits in AllNeutralUnits so TurnManager's
    /// environment-turn pass can iterate them without scanning AllUnits.
    /// </summary>
    public class NeutralUnit : Unit
    {
        [Header("Neutral Unit Abilities")]
        [Tooltip("Fired once at the end of every round during the environment turn. " +
                 "Leave null for a neutral unit that only reacts to destruction.")]
        public Ability environmentAbility;

        [Tooltip("Fired automatically when this unit's health reaches zero, before the " +
                 "down sequence begins. Leave null for no destruction effect.")]
        public Ability destructionAbility;

        [Header("Spawn Source (set at runtime by SpawnNeutralUnitEffect)")]
        [Tooltip("The effect asset that spawned this unit. Set automatically at runtime — " +
                 "do not assign in the Inspector. Used to notify the effect when this unit " +
                 "is destroyed so it can clear its active-instance reference.")]
        public SpawnNeutralUnitEffect spawnSource;

        // ── IsNeutral override ────────────────────────────────────────────────

        /// <summary>
        /// NeutralUnits are always neutral — unlike PlayerUnit / EnemyUnit whose
        /// IsNeutral only becomes true after they die and become a body.
        /// </summary>
        public override bool IsNeutral => true;

        // ── Down override — fire destructionAbility first ────────────────────

        /// <summary>
        /// Overrides ReceiveDamage to intercept the lethal hit and fire destructionAbility
        /// before the base down path runs. The destruction coroutine completes during
        /// UnitDownedSequencer.DrainDownedQueue so the ability resolves before the unit vanishes.
        /// </summary>
        public override void ReceiveDamage(int amount, Unit attacker = null, Ability sourceAbility = null)
        {
            // Let the base class handle all status-effect checks (Shielded, Immune, etc.)
            // and health subtraction. We intercept only when the hit is lethal.
            int healthBefore = currentHealth;
            base.ReceiveDamage(amount, attacker, sourceAbility);

            // If the unit just died AND has a destruction ability, fire it.
            // IsDead becomes true inside base.ReceiveDamage → Die(), so we check it here.
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

        // ── Cleanup ───────────────────────────────────────────────────────────

        protected virtual void OnDestroy()
        {
            // Notify the spawning effect that this instance is gone so it can clear
            // its _activeInstance reference and allow the ability to be used again.
            spawnSource?.OnInstanceDestroyed(this);
        }
    }
}