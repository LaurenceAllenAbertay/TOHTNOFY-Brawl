using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Ghost")]
    public class GhostPassive : PassiveAbility
    {
        [Header("Untargetable On Kill")]
        public StatusEffectData untargetableData;
        public int untargetableDuration = 1;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            System.Action<Unit, Unit> onKilled = (victim, killer) =>
            {
                if (killer != handler.Owner) return;
                if (victim == handler.Owner) return;
                if (victim.IsNeutral) return;
                if (victim.IsAllyOf(handler.Owner)) return;
                if (handler.Owner.IsDead) return;
                if (untargetableData == null || StatusEffectManager.Instance == null) return;

                StatusEffectManager.Instance.ApplyStatusEffect(
                    handler.Owner,
                    untargetableData,
                    source: handler.Owner,
                    duration: untargetableDuration);
            };

            UnitManager.OnUnitKilled += onKilled;
            RegisterCleanup(() => UnitManager.OnUnitKilled -= onKilled);
        }
    }
}