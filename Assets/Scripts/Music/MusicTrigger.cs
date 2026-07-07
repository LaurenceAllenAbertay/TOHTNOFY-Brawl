using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [System.Serializable]
    public class MusicTrigger
    {
        public enum TriggerType
        {
            UnitType, 
            CombatState, 
            TurnNumber,
            HealthThreshold, 
            CustomEvent
        }

        public enum TurnCountMode
        {
            CyclicTurns, 
            TotalTurns
        }

        public TriggerType triggerType;

        [Header("General Settings")]
        public bool activateOnce = false;

        [Header("Unit Type Trigger")] public string unitTypeName; 

        [Header("Combat State Trigger")] public CombatState combatState;

        public string combatStateUnitFilter = ""; 

        [Header("Turn Number Trigger")] public TurnCountMode turnCountMode = TurnCountMode.CyclicTurns;
        public int turnNumber;
        public bool everyNthTurn = false; 

        [Header("Health Threshold Trigger")] [Range(0f, 1f)]
        public float healthPercentage = 0.3f;

        [Header("Custom Event Trigger")] public string eventName;
        
        [System.NonSerialized] public bool hasBeenActivated = false;
    }
}