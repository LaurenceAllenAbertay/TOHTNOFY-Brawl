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
    }
}