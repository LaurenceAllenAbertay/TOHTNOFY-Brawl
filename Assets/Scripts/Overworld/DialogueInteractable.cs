using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class DialogueInteractable : OverworldInteractable
    {
        [Header("Dialogue")]
        [SerializeField] private DialogueConversationSet conversationSet;

        [SerializeField] private DialogueConversationSet repeatConversationSet;

        [SerializeField] private bool playOnce = false;

        private bool _hasPlayed;

        private DialogueConversationSet ActiveSet
        {
            get
            {
                if (!_hasPlayed) return conversationSet;
                if (playOnce) return null;

                return repeatConversationSet != null ? repeatConversationSet : conversationSet;
            }
        }

        protected override bool CanInteract =>
            OverworldDialogueManager.Instance != null && ResolveConversation() != null;

        protected override void OnInteract()
        {
            if (OverworldDialogueManager.Instance.TryPlay(ResolveConversation()))
                _hasPlayed = true;
        }

        private DialogueConversation ResolveConversation()
        {
            var set = ActiveSet;
            if (set == null) return null;

            var party = OverworldPartyManager.Instance;

            return set.Resolve(party != null ? party.LeaderCharacter : null,
                party != null ? party.PartyCharacters : null);
        }
    }
}