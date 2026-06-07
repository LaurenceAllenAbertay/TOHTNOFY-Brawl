using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Player-controlled unit. All turn, movement, and combat behaviour
    /// is inherited from Unit. This class exists as a distinct type so systems
    /// can distinguish player units from enemy units via 'is PlayerUnit' checks
    /// (e.g. CombatManager input gating, TurnManager UI events).
    /// Add player-specific overrides here as the game requires them.
    /// </summary>
    public class PlayerUnit : Unit
    {
    }
}