using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Mastermind")]
    public class MastermindPassive : PassiveAbility
    {
        [Header("Potency Boost")]
        public float potencyBonus = 1f;

        [Header("Mirror Buff")]
        public int mirrorRange = 5;

        [Header("Mirror Buff Status Effect Data")]
        public StatusEffectData attackUpData;
        public StatusEffectData defenseUpData;
        public StatusEffectData speedUpData;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            System.Func<Unit, Unit, StatusEffectData, float, float> powerModifier =
                (source, target, effectData, power) =>
                {
                    if (source == handler.Owner)
                        return power + potencyBonus;
                    return power;
                };
            StatusEffectManager.OnModifyEffectPower += powerModifier;
            RegisterCleanup(() => StatusEffectManager.OnModifyEffectPower -= powerModifier);

            System.Action<Unit, StatusEffectInstance> statusApplied =
                (target, effect) => TryMirrorBuff(handler.Owner, target, effect);
            StatusEffectManager.OnStatusEffectApplied += statusApplied;
            RegisterCleanup(() => StatusEffectManager.OnStatusEffectApplied -= statusApplied);
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            base.Cleanup(handler);
        }

        private void TryMirrorBuff(Unit lorns, Unit target, StatusEffectInstance effect)
        {
            if (lorns == null) return;
            
            if (target == lorns) return;
            
            StatusEffectData mirrorData = GetMirrorData(effect.effectData.effectType);
            if (mirrorData == null) return;
            
            if (!lorns.IsAllyOf(target)) return;
            
            if (lorns.currentTile == null || target.currentTile == null) return;
            int dist = GridManager.Instance.GetGridDistance(lorns.currentTile, target.currentTile);
            if (dist > mirrorRange) return;
            
            StatusEffectManager.Instance.ApplyStatusEffect(
                lorns,
                mirrorData,
                source: target,
                duration: effect.remainingDuration,
                power: 1f);
        }

        private StatusEffectData GetMirrorData(StatusEffectType effectType)
        {
            switch (effectType)
            {
                case StatusEffectType.AttackUp:  return attackUpData;
                case StatusEffectType.DefenseUp: return defenseUpData;
                case StatusEffectType.SpeedUp:   return speedUpData;
                default: return null;
            }
        }
    }
}