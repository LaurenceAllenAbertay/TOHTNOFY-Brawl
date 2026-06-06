using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Brodie Boots Passive
    ///
    /// Part 1 — Extended jump range: adds jumpRangeBonus to Unit.JumpRange on initialise.
    /// JumpSystem, AIEvaluator, and AIExecutor all read from Unit.JumpRange, so the wider
    /// range is automatically respected for both player-controlled and AI-controlled Brodie.
    /// Units may always jump any distance from 2 up to their JumpRange, so a range of 4
    /// allows landing 2, 3, or 4 tiles away — not just at the maximum.
    ///
    /// Part 2 — Stomp: sets Unit.CanStompOccupiedTiles to true.
    /// When true, JumpSystem allows Brodie to land on occupied tiles. On landing,
    /// JumpSystem.ExecuteStomp deals damage equal to Brodie's attack (minus target defense)
    /// and knocks the victim 1 tile in the jump direction. AIEvaluator includes occupied
    /// tiles in its jumpable-tile search so AI Brodie will consider stomps too.
    /// </summary>
    [CreateAssetMenu(menuName = "TNFY Brawl/Passives/Brodie Boots")]
    public class BrodieBootsPassive : PassiveAbility
    {
        [Header("Jump Range")]
        [Tooltip("How many extra tiles are added to Brodie's jump range.")]
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
        }
    }
}