using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Abstract base ScriptableObject for passive abilities.
    /// Each concrete passive overrides Initialise and Cleanup.
    /// The PassiveAbilityHandler MonoBehaviour on the unit calls these
    /// at the right points in the unit lifecycle.
    /// </summary>
    public abstract class PassiveAbility : ScriptableObject
    {
        [Header("Info")]
        public string passiveName;
        [TextArea] public string description;
        public Sprite icon;

        /// <summary>
        /// Called once when combat begins (after the unit is fully initialised).
        /// Subscribe to events and apply any permanent stat changes here.
        /// </summary>
        public abstract void Initialise(PassiveAbilityHandler handler);

        /// <summary>
        /// Called when the unit is destroyed or combat ends.
        /// Unsubscribe from events and reverse any stat changes here.
        /// </summary>
        public abstract void Cleanup(PassiveAbilityHandler handler);
    }
}