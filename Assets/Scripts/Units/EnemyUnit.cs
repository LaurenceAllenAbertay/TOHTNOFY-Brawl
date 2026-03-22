using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class EnemyUnit : Unit
    {

        public override void StartTurn()
        {
            base.StartTurn();
        }

        public override void EndTurn()
        {
            base.EndTurn();
        }

        #region Generic Targeting System

        // Generic method to add a unit to either targeting dictionary
        private void AddUnitToDictionary(Dictionary<Unit, int> dictionary, Unit unit, int duration, string logMessage)
        {
            if (unit == null) return;

            if (dictionary.ContainsKey(unit))
            {
                dictionary[unit] = Mathf.Max(dictionary[unit], duration);
            }
            else
            {
                dictionary[unit] = duration;
            }
        }

        // Generic method to remove a unit from either targeting dictionary
        private bool RemoveUnitFromDictionary(Dictionary<Unit, int> dictionary, Unit unit, string logMessage)
        {
            if (unit != null && dictionary.ContainsKey(unit))
            {
                dictionary.Remove(unit);
                Debug.Log(string.Format(logMessage, name, unit.name));
                return true;
            }
            return false;
        }

        // Generic method to update durations for any targeting dictionary
        private void UpdateDictionaryDurations(Dictionary<Unit, int> dictionary, System.Action<Unit> removeCallback)
        {
            var unitsToRemove = new List<Unit>();
            var keys = new List<Unit>(dictionary.Keys);

            foreach (var unit in keys)
            {
                if (unit == null)
                {
                    unitsToRemove.Add(unit);
                    continue;
                }

                dictionary[unit]--;
                if (dictionary[unit] <= 0)
                {
                    unitsToRemove.Add(unit);
                }
            }

            foreach (var unit in unitsToRemove)
            {
                removeCallback(unit);
            }
        }

        #endregion

        #region Debug Visualization

        private void DrawTargetingGizmos(Dictionary<Unit, int> dictionary, Color color)
        {
            if (dictionary.Count == 0) return;

            Gizmos.color = color;
            foreach (var kvp in dictionary)
            {
                if (kvp.Key != null)
                {
                    Gizmos.DrawLine(transform.position, kvp.Key.transform.position);
                }
            }
        }

        #endregion
    }
}