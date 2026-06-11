using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// AI-controlled enemy unit. All turn, movement, and combat behaviour
    /// is inherited from Unit. This class exists as a distinct type so systems
    /// can distinguish enemy units from player units via 'is EnemyUnit' checks
    /// (e.g. CombatManager input gating, AI targeting).
    /// Add enemy-specific overrides here as the game requires them.
    /// </summary>
    public class EnemyUnit : Unit
    {
        [Header("Death Behaviour")]
        [Tooltip("If true, this enemy leaves a neutral body on the map after dying. " +
                 "The body blocks movement and can be targeted by abilities with canTargetNeutral. " +
                 "Disable for enemies that should vanish cleanly on death (e.g. summoned minions).")]
        public bool leavesBodyOnDeath = true;
    }
}