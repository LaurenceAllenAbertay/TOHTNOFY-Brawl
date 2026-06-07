using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Lone Wolf Passive (Lorns)
    ///
    /// Effect 1 - Permanent speed bonus: +2 to currentSpeed on initialise, reversed on cleanup.
    ///
    /// Effect 2 - Conditional AttackUp: after every unit's turn ends, check whether any ally
    /// is within 3 tiles of Lorns. If none are, apply AttackUp. If one is, remove it.
    /// The effect is re-evaluated every turn so it responds immediately to ally movement.
    ///
    /// The AttackUp StatusEffectData asset and power are configured on this SO in the Inspector
    /// so values can be tuned without touching code.
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Lone Wolf")]
    public class LoneWolfPassive : PassiveAbility
    {
        [Header("Speed Bonus")]
        public int speedBonus = 2;

        [Header("Lone Wolf Attack Buff")]
        public StatusEffectData attackUpData;
        public float attackPower = 3f;
        public int allyProximityRange = 3;

        // Reference kept so we can remove the effect when an ally comes back in range.
        private StatusEffectInstance activeAttackBuff;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            // Effect 1: permanent speed bonus.
            handler.Owner.currentSpeed += speedBonus;

            // Effect 2: subscribe to turn end and unit movement to re-evaluate ally proximity.
            // OnUnitMoved covers Lorns moving, teleporting, being knocked back, etc.
            System.Action<Unit> onTurnEnded = _ => EvaluateLoneWolf(handler.Owner);
            TurnManager.OnTurnEnded += onTurnEnded;
            RegisterCleanup(() => TurnManager.OnTurnEnded -= onTurnEnded);

            System.Action<Unit> onUnitMoved = movedUnit =>
            {
                if (movedUnit == handler.Owner) EvaluateLoneWolf(handler.Owner);
            };
            UnitManager.OnUnitMoved += onUnitMoved;
            RegisterCleanup(() => UnitManager.OnUnitMoved -= onUnitMoved);

            // Run an initial evaluation in case combat starts with no allies nearby.
            EvaluateLoneWolf(handler.Owner);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            // Reverse the speed bonus.
            handler.Owner.currentSpeed -= speedBonus;

            // Remove the attack buff if it is still active.
            RemoveAttackBuff(handler.Owner);

            base.Cleanup(handler);
        }

        private void EvaluateLoneWolf(Unit owner)
        {
            if (owner == null) return;

            bool allyNearby = IsAllyWithinRange(owner);

            if (!allyNearby && activeAttackBuff == null)
            {
                // No ally nearby and buff not yet active — apply it.
                ApplyAttackBuff(owner);
            }
            else if (allyNearby && activeAttackBuff != null)
            {
                // Ally came within range — remove the buff.
                RemoveAttackBuff(owner);
            }
            // If state hasn't changed, do nothing.
        }

        private bool IsAllyWithinRange(Unit owner)
        {
            if (owner.currentTile == null) return false;

            foreach (Unit unit in UnitManager.AllUnits)
            {
                // Skip self and enemies.
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

            // Duration -1 signals indefinite — the passive manages removal itself.
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