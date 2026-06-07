using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [System.Serializable]
    public class MusicTrigger
    {
        public enum TriggerType
        {
            UnitType, // When specific unit type's turn starts
            CombatState, // When combat state changes
            TurnNumber, // On specific turn numbers
            HealthThreshold, // When any unit's health drops below threshold
            CustomEvent // Custom named events
        }

        public enum TurnCountMode
        {
            CyclicTurns, // 1,2,3,4,1,2,3,4,1,2,... (resets after each round)
            TotalTurns // 1,2,3,4,5,6,7,8,9,... (continuously increments)
        }

        public TriggerType triggerType;

        [Header("General Settings")]
        [Tooltip("If true, this trigger can only activate once and will be disabled after first activation")]
        public bool activateOnce = false;

        [Header("Unit Type Trigger")] public string unitTypeName; // "PlayerUnit", "EnemyUnit", or specific unit names

        [Header("Combat State Trigger")] public CombatState combatState;

        [Tooltip("Leave empty to trigger for any unit, or specify unit name/type to filter")]
        public string combatStateUnitFilter = ""; // Filter by specific unit name or type

        [Header("Turn Number Trigger")] public TurnCountMode turnCountMode = TurnCountMode.CyclicTurns;
        public int turnNumber;
        public bool everyNthTurn = false; // If true, triggers every N turns

        [Header("Health Threshold Trigger")] [Range(0f, 1f)]
        public float healthPercentage = 0.3f;

        [Header("Custom Event Trigger")] public string eventName;

        // Runtime data - tracks if this trigger has been activated (for activateOnce)
        [System.NonSerialized] public bool hasBeenActivated = false;
    }
}