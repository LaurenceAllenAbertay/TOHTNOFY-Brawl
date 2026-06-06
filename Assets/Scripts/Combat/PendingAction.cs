using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Stores a follow-up ability that will auto-execute at the start of the unit's next turn.
    /// Set by QueuedActionEffect. Checked by CombatManager and UnitAI at turn start.
    /// </summary>
    public struct PendingAction
    {
        public Ability ability;
        public Vector2Int aimDir;

        public PendingAction(Ability ability, Vector2Int aimDir)
        {
            this.ability = ability;
            this.aimDir = aimDir;
        }
    }
}