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

        // Stored so we can unsubscribe with the exact same delegate instance.
        private System.Action<Unit> onTurnEndedHandler;
        private System.Action<Unit> onUnitMovedHandler;

        // Reference kept so we can remove the effect when an ally comes back in range.
        private StatusEffectInstance activeAttackBuff;

        // Cached handler reference — needed to access Owner inside the lambda.
        private PassiveAbilityHandler cachedHandler;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            cachedHandler = handler;

            // Effect 1: permanent speed bonus.
            handler.Owner.currentSpeed += speedBonus;

            // Effect 2: subscribe to turn end and unit movement to re-evaluate ally proximity.
            // OnUnitMoved covers Lorns moving, teleporting, being knocked back, etc.
            onTurnEndedHandler = _ => EvaluateLoneWolf();
            TurnManager.OnTurnEnded += onTurnEndedHandler;

            onUnitMovedHandler = movedUnit =>
            {
                if (movedUnit == handler.Owner) EvaluateLoneWolf();
            };
            UnitManager.OnUnitMoved += onUnitMovedHandler;

            // Run an initial evaluation in case combat starts with no allies nearby.
            EvaluateLoneWolf();
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            // Reverse the speed bonus.
            handler.Owner.currentSpeed -= speedBonus;

            // Unsubscribe to avoid dangling event references.
            if (onTurnEndedHandler != null)
            {
                TurnManager.OnTurnEnded -= onTurnEndedHandler;
                onTurnEndedHandler = null;
            }

            if (onUnitMovedHandler != null)
            {
                UnitManager.OnUnitMoved -= onUnitMovedHandler;
                onUnitMovedHandler = null;
            }

            // Remove the attack buff if it is still active.
            RemoveAttackBuff();

            cachedHandler = null;
        }

        private void EvaluateLoneWolf()
        {
            if (cachedHandler == null || cachedHandler.Owner == null) return;

            bool allyNearby = IsAllyWithinRange();

            if (!allyNearby && activeAttackBuff == null)
            {
                // No ally nearby and buff not yet active — apply it.
                ApplyAttackBuff();
            }
            else if (allyNearby && activeAttackBuff != null)
            {
                // Ally came within range — remove the buff.
                RemoveAttackBuff();
            }
            // If state hasn't changed, do nothing.
        }

        private bool IsAllyWithinRange()
        {
            Unit owner = cachedHandler.Owner;
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

        private void ApplyAttackBuff()
        {
            if (attackUpData == null || StatusEffectManager.Instance == null) return;

            Unit owner = cachedHandler.Owner;

            // Duration -1 signals indefinite — the passive manages removal itself.
            activeAttackBuff = StatusEffectManager.Instance.ApplyStatusEffect(
                owner, attackUpData, owner, duration: -1, power: attackPower);
        }

        private void RemoveAttackBuff()
        {
            if (activeAttackBuff == null || StatusEffectManager.Instance == null) return;

            StatusEffectManager.Instance.RemoveStatusEffect(
                cachedHandler?.Owner, activeAttackBuff);
            activeAttackBuff = null;
        }
    }
}