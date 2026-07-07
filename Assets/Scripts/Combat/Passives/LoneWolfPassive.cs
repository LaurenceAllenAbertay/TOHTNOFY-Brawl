using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Lone Wolf")]
    public class LoneWolfPassive : PassiveAbility
    {
        [Header("Speed Bonus")]
        public int speedBonus = 2;

        [Header("Lone Wolf Attack Buff")]
        public StatusEffectData attackUpData;
        public float attackPower = 3f;
        public int allyProximityRange = 3;
        
        private StatusEffectInstance activeAttackBuff;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            handler.Owner.currentSpeed += speedBonus;
            
            System.Action<Unit> onTurnEnded = _ => EvaluateLoneWolf(handler.Owner);
            TurnManager.OnTurnEnded += onTurnEnded;
            RegisterCleanup(() => TurnManager.OnTurnEnded -= onTurnEnded);

            System.Action<Unit> onUnitMoved = movedUnit =>
            {
                if (movedUnit == handler.Owner) EvaluateLoneWolf(handler.Owner);
            };
            UnitManager.OnUnitMoved += onUnitMoved;
            RegisterCleanup(() => UnitManager.OnUnitMoved -= onUnitMoved);
            
            EvaluateLoneWolf(handler.Owner);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            handler.Owner.currentSpeed -= speedBonus;
            
            RemoveAttackBuff(handler.Owner);

            base.Cleanup(handler);
        }

        private void EvaluateLoneWolf(Unit owner)
        {
            if (owner == null) return;

            bool allyNearby = IsAllyWithinRange(owner);

            if (!allyNearby && activeAttackBuff == null)
            {
                ApplyAttackBuff(owner);
            }
            else if (allyNearby && activeAttackBuff != null)
            {
                RemoveAttackBuff(owner);
            }
        }

        private bool IsAllyWithinRange(Unit owner)
        {
            if (owner.currentTile == null) return false;

            foreach (Unit unit in UnitManager.AllUnits)
            {
                if (unit == owner) continue;
                if (!owner.IsAllyOf(unit)) continue;
                if (unit.currentTile == null) continue;

                int dist = GridManager.Instance.GetGridDistance(owner.currentTile, unit.currentTile);
                if (dist <= allyProximityRange)
                    return true;
            }
            return false;
        }

        private void ApplyAttackBuff(Unit owner)
        {
            if (attackUpData == null || StatusEffectManager.Instance == null) return;
            
            activeAttackBuff = StatusEffectManager.Instance.ApplyStatusEffect(
                owner, attackUpData, owner, duration: -1, power: attackPower);
        }

        private void RemoveAttackBuff(Unit owner)
        {
            if (activeAttackBuff == null || StatusEffectManager.Instance == null) return;

            StatusEffectManager.Instance.RemoveStatusEffect(owner, activeAttackBuff);
            activeAttackBuff = null;
        }
    }
}