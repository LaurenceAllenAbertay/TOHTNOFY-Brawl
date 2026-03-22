using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class TurnManager : MonoBehaviour
    {
        public static event System.Action<Unit> OnTurnStarted;  // When a unit's turn begins
        public static event System.Action<Unit> OnTurnEnded;    // When a unit's turn ends
        public static event System.Action<int, int> OnTurnNumberChanged; // When turn index changes
        public static event System.Action<int> OnTotalTurnChanged; // When total turn count changes

        private List<Unit> turnOrder = new List<Unit>();
        private int currentIndex = 0;
        [SerializeField] private int totalTurnCount = 0;
        public TextMeshProUGUI turnOrderText;

        // Reference to CombatManager for handling combat actions
        private CombatManager combatManager;

        // Properties
        public Unit CurrentUnit => turnOrder.Count > 0 ? turnOrder[currentIndex] : null;
        public List<Unit> TurnOrder => turnOrder;
        public int CurrentTurnIndex => currentIndex;
        public int TotalTurnCount => totalTurnCount;

        void Start()
        {
            combatManager = FindObjectOfType<CombatManager>();
            BuildTurnOrder();
            StartNextTurn();
        }

        void BuildTurnOrder()
        {
            var allUnits = FindObjectsOfType<Unit>();
            // Only include PlayerUnit and EnemyUnit
            var validUnits = allUnits.Where(u => u is PlayerUnit || u is EnemyUnit);

            // Roll initiative
            var rolled = validUnits.Select(u => new
            {
                unit = u,
                initiative = Random.Range(0, u.currentSpeed + 1)
            });

            // Sort by initiative, break ties randomly
            turnOrder = rolled
                .OrderByDescending(x => x.initiative)
                .ThenBy(x => Random.value)
                .Select(x => x.unit)
                .ToList();

            // Debug Mode
            if (turnOrderText == null) return;
            turnOrderText.text = "Turn Order:";
            foreach (var unit in turnOrder)
            {
                turnOrderText.text += $"\n- {unit.gameObject.name}";
            }
        }

        void StartNextTurn()
        {
            if (turnOrder.Count == 0)
            {
                Debug.LogWarning("TurnManager: No units in turn order!");
                return;
            }

            // Increment total turn count at the start of each turn
            totalTurnCount++;

            Unit current = CurrentUnit;

            var unitAnimator = current.GetComponent<UnitAnimator>();
            if (unitAnimator != null)
            {
                unitAnimator.SetActiveTurn();
            }

            if (current is PlayerUnit player)
                player.StartTurn();
            else if (current is EnemyUnit enemy)
                enemy.StartTurn();

            // Notify CombatManager about the new active unit
            if (combatManager != null)
                combatManager.OnTurnStarted(current);

            // FIRE THE EVENTS
            OnTurnStarted?.Invoke(current);
            OnTotalTurnChanged?.Invoke(totalTurnCount);
        }

        public void EndTurn()
        {
            Unit currentUnit = CurrentUnit;

            var unitAnimator = currentUnit?.GetComponent<UnitAnimator>();
            if (unitAnimator != null)
            {
                unitAnimator.SetInactiveTurn();
            }

            // This forces the UI to collapse abilities and prepare for next unit
            if (currentUnit is PlayerUnit)
            {
                UIEvents.OnTurnChanged();
            }

            // Notify current unit that turn is ending
            currentUnit?.EndTurn();

            // FIRE THE TURN ENDED EVENT
            OnTurnEnded?.Invoke(currentUnit);

            currentIndex++;
            if (currentIndex >= turnOrder.Count)
            {
                currentIndex = 0;
                TriggerEnvironmentEffects();
            }

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);

            StartNextTurn();
        }

        void TriggerEnvironmentEffects()
        {
            //tile.TriggerEnvironmentEffect();
        }

        // Public method to reset total turn count (useful for new battles)
        public void ResetTotalTurnCount()
        {
            totalTurnCount = 0;
        }

        // Public method to get current round number (how many complete cycles through all units)
        public int GetCurrentRound()
        {
            if (turnOrder.Count == 0) return 0;
            return (totalTurnCount - 1) / turnOrder.Count + 1;
        }

        // Public method to get turn within current round (1-based)
        public int GetTurnInCurrentRound()
        {
            if (turnOrder.Count == 0) return 0;
            return ((totalTurnCount - 1) % turnOrder.Count) + 1;
        }
    }
}