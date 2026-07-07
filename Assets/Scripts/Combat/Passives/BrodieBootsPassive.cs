using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Brodie Boots")]
    public class BrodieBootsPassive : PassiveAbility
    {
        [Header("Jump Range")]
        public int jumpRangeBonus = 2;

        public override void Initialise(PassiveAbilityHandler handler)
        {
            handler.Owner.JumpRange += jumpRangeBonus;
            handler.Owner.CanStompOccupiedTiles = true;
        }

        public override void Cleanup(PassiveAbilityHandler handler)
        {
            handler.Owner.JumpRange -= jumpRangeBonus;
            handler.Owner.CanStompOccupiedTiles = false;
            base.Cleanup(handler);
        }
    }
}