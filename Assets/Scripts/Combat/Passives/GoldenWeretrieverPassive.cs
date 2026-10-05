using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Golden Weretriever")]
    public class GoldenWeretrieverPassive : PassiveAbility
    {
        [Header("Transformed Form")]
        public CharacterData transformedForm;

        public RuntimeAnimatorController transformedController;

        [Header("Animation")]
        public string transformAnimationState = "Transform";

        [Header("Trigger")]
        public int survivingTeammateThreshold = 2;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            System.Action<Unit> onUnitDied = deadUnit =>
            {
                var owner = handler.Owner;

                if (owner == null || owner.IsDead || owner.IsBody) return;
                if (owner.HasTransformed) return;
                if (deadUnit == null || deadUnit == owner) return;
                if (!deadUnit.IsAllyOf(owner)) return;

                if (CountLivingTeammates(owner) != survivingTeammateThreshold) return;

                if (transformedForm == null)
                {
                    Debug.LogWarning($"[GoldenWeretriever] {owner.name} has no Transformed Form assigned — " +
                                     "transformation skipped.");
                    return;
                }

                if (TransformationSequencer.Instance == null)
                {
                    Debug.LogWarning("[GoldenWeretriever] No TransformationSequencer in the scene — " +
                                     "transforming immediately without a sequence.");
                    owner.TransformInto(transformedForm);
                    return;
                }

                TransformationSequencer.Instance.Enqueue(
                    owner, transformedForm, transformedController, transformAnimationState);
            };

            UnitManager.OnUnitDied += onUnitDied;
            RegisterCleanup(() => UnitManager.OnUnitDied -= onUnitDied);
        }

        private static int CountLivingTeammates(Unit owner)
        {
            int count = 0;

            foreach (var unit in UnitManager.AllUnits)
            {
                if (unit == null || unit.IsDead || unit.IsBody || unit.IsNeutral) continue;
                if (!unit.IsAllyOf(owner)) continue;

                count++;
            }

            return count;
        }
    }
}