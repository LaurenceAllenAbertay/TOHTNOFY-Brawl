using UnityEngine;

namespace DDD.TNFY.BRAWL
{
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