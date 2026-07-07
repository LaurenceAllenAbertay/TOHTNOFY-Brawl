using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class PassiveAbility : ScriptableObject
    {
        [Header("Info")]
        public string passiveName;
        [TextArea] public string description;
        public Sprite icon;
        
        private readonly List<System.Action> _cleanupActions = new List<System.Action>();

        protected void RegisterCleanup(System.Action cleanupAction)
        {
            if (cleanupAction != null)
                _cleanupActions.Add(cleanupAction);
        }
        
        public abstract void Initialise(PassiveAbilityHandler handler);
        
        public virtual void Cleanup(PassiveAbilityHandler handler)
        {
            foreach (var action in _cleanupActions)
                action?.Invoke();
            _cleanupActions.Clear();
        }
    }
}