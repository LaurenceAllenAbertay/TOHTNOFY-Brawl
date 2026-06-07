using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Abstract base ScriptableObject for passive abilities.
    /// Each concrete passive overrides Initialise and optionally Cleanup.
    /// The PassiveAbilityHandler MonoBehaviour on the unit calls these
    /// at the right points in the unit lifecycle.
    ///
    /// ── Safe event subscription pattern ──────────────────────────────────────
    /// In Initialise, call RegisterCleanup() immediately after each event subscription:
    ///
    ///     System.Action<Unit> handler = unit => { ... };
    ///     TurnManager.OnTurnEnded += handler;
    ///     RegisterCleanup(() => TurnManager.OnTurnEnded -= handler);
    ///
    /// The base Cleanup automatically runs every registered action, so a concrete
    /// class only needs to override Cleanup to reverse non-event changes (stat
    /// modifications, flag resets, etc.) and then call base.Cleanup(handler).
    /// Forgetting to call base.Cleanup is the only remaining footgun — the comment
    /// on Cleanup makes this explicit.
    /// </summary>
    public abstract class PassiveAbility : ScriptableObject
    {
        [Header("Info")]
        public string passiveName;
        [TextArea] public string description;
        public Sprite icon;

        // Cleanup actions registered during Initialise. Populated via RegisterCleanup.
        private readonly List<System.Action> _cleanupActions = new List<System.Action>();

        /// <summary>
        /// Registers an action to be run automatically when Cleanup is called.
        /// Call this immediately after every event subscription in Initialise so
        /// the unsubscription is guaranteed to use the exact same delegate instance.
        ///
        /// Example:
        ///     System.Action&lt;Unit&gt; handler = _ => DoThing();
        ///     TurnManager.OnTurnEnded += handler;
        ///     RegisterCleanup(() => TurnManager.OnTurnEnded -= handler);
        /// </summary>
        protected void RegisterCleanup(System.Action cleanupAction)
        {
            if (cleanupAction != null)
                _cleanupActions.Add(cleanupAction);
        }

        /// <summary>
        /// Called once when combat begins (after the unit is fully initialised).
        /// Subscribe to events and apply any permanent stat changes here.
        /// Use RegisterCleanup() immediately after each subscription.
        /// </summary>
        public abstract void Initialise(PassiveAbilityHandler handler);

        /// <summary>
        /// Called when the unit is destroyed or combat ends.
        /// Override to reverse stat changes and any non-event cleanup, then
        /// ALWAYS call base.Cleanup(handler) — this runs all registered unsubscriptions.
        /// </summary>
        public virtual void Cleanup(PassiveAbilityHandler handler)
        {
            foreach (var action in _cleanupActions)
                action?.Invoke();
            _cleanupActions.Clear();
        }
    }
}