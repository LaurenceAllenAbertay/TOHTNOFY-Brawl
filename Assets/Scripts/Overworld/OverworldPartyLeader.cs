namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Marker component placed on the party leader's GameObject by DebugOverworldSpawner.
    ///
    /// OverworldInteractable checks for this component in OnTriggerEnter/Exit to
    /// determine whether the leader (not a follower) has entered its proximity zone.
    ///
    /// Contains no logic — exists purely as a type-safe tag.
    /// </summary>
    public class OverworldPartyLeader : UnityEngine.MonoBehaviour { }
}