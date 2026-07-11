using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class ActionPlan
    {
        public Tile movementTarget;

        public bool isJump;

        public Ability abilityToUse;

        public int abilitySlot = -1;

        public Vector2Int aimDirection;

        public Tile targetTile;

        public List<Tile> preSelectedTiles;

        public bool isAbilityFirst;

        public string debugReason;
    }
}