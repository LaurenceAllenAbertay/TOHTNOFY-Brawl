using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Manages the four fixed turn-indicator buttons in the HUD.
    ///
    /// Slot 0 = current unit, Slots 1-3 = next three units (wraps around the turn order).
    /// Each slot shows the unit's portrait and pans the camera to that unit on click.
    /// When a unit dies their portrait is tinted grey.
    ///
    /// The four Button GameObjects are pre-placed in the scene and assigned via the Inspector.
    /// Attach to the same GameObject as UIManager.
    /// </summary>
    public class TurnOrderUIController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Turn Indicator Buttons (Slot 0 = Current, 1-3 = Upcoming)")]
        [SerializeField] private Button slot0Button;
        [SerializeField] private Button slot1Button;
        [SerializeField] private Button slot2Button;
        [SerializeField] private Button slot3Button;

        #endregion

        #region Private Fields

        private TurnManager turnManager;
        private CombatManager combatManager;

        // Injected by UIManager so the controller knows when UI should be interactive
        private System.Func<bool> isUIBlockedForAnimation;
        private System.Func<bool> isUIBlockedForMovement;

        // The four slots in order — populated from the serialized fields above
        private Button[] slotButtons;

        // Tracks which unit is currently assigned to each slot so we can grey out
        // the correct portrait when UnitManager.OnUnitDied fires
        private Unit[] slotUnits;

        private static readonly Color AliveColor = Color.white;
        private static readonly Color DeadColor  = new Color(0.35f, 0.35f, 0.35f, 1f);

        #endregion

        #region Initialization

        public void Initialize(
            TurnManager turnManager,
            CombatManager combatManager,
            System.Func<bool> isUIBlockedForAnimation,
            System.Func<bool> isUIBlockedForMovement)
        {
            this.turnManager              = turnManager;
            this.combatManager            = combatManager;
            this.isUIBlockedForAnimation  = isUIBlockedForAnimation;
            this.isUIBlockedForMovement   = isUIBlockedForMovement;

            slotButtons = new Button[] { slot0Button, slot1Button, slot2Button, slot3Button };
            slotUnits   = new Unit[4];

            UnitManager.OnUnitDied += HandleUnitDied;
        }

        private void OnDestroy()
        {
            UnitManager.OnUnitDied -= HandleUnitDied;
        }

        #endregion

        #region Public API — called by UIManager event handlers

        public void HandleTurnStarted(Unit unit)
        {
            RefreshAllSlots();
        }

        public void HandleTurnIndexChanged(int totalTurns, int currentIndex)
        {
            RefreshAllSlots();
        }

        #endregion

        #region Slot Refresh

        /// <summary>
        /// Reads the current turn order from TurnManager and assigns the correct unit
        /// portrait + click listener to each of the four slots.
        /// </summary>
        private void RefreshAllSlots()
        {
            if (turnManager == null) return;

            IReadOnlyList<Unit> order = turnManager.TurnOrder;
            int count                 = order.Count;
            int currentIndex          = turnManager.CurrentTurnIndex;

            for (int slot = 0; slot < 4; slot++)
            {
                Button button = slotButtons[slot];
                if (button == null) continue;

                int unitIndex = (currentIndex + slot) % count;
                Unit unit     = order[unitIndex];

                slotUnits[slot] = unit;

                SetSlotPortrait(button, unit);
                SetSlotClickListener(button, unit);
                SetSlotTint(button, unit);
            }

            UpdateAllButtonInteractability();
        }

        /// <summary>
        /// Assigns the unit's portrait sprite to the portrait Image child of the button.
        /// Falls back to the unit's SpriteRenderer sprite if no portrait is set on CharacterData.
        /// </summary>
        private void SetSlotPortrait(Button button, Unit unit)
        {
            Image portraitImage = button.GetComponentInChildren<Image>();
            if (portraitImage == null || unit == null) return;

            if (unit.characterData != null && unit.characterData.portrait != null)
            {
                portraitImage.sprite = unit.characterData.portrait;
            }
            else
            {
                var sr = unit.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                    portraitImage.sprite = sr.sprite;
            }
        }

        /// <summary>
        /// Wires the button's onClick to pan the camera to the unit assigned to this slot.
        /// Captures unit by value so the lambda always targets the right unit regardless of
        /// later slot reassignments.
        /// </summary>
        private void SetSlotClickListener(Button button, Unit unit)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnSlotClicked(unit));
        }

        /// <summary>
        /// Tints the portrait grey if the unit is dead, white if alive.
        /// </summary>
        private void SetSlotTint(Button button, Unit unit)
        {
            Image portraitImage = button.GetComponentInChildren<Image>();
            if (portraitImage == null) return;
            portraitImage.color = (unit != null && unit.IsDead) ? DeadColor : AliveColor;
        }

        #endregion

        #region Unit Death

        /// <summary>
        /// Called by UnitManager.OnUnitDied. Finds which slot(s) the dead unit occupies
        /// and greys out their portrait — does not shift the slot contents since TurnManager
        /// has already removed the unit and HandleTurnStarted will refresh on the next turn.
        /// </summary>
        private void HandleUnitDied(Unit unit)
        {
            for (int slot = 0; slot < slotUnits.Length; slot++)
            {
                if (slotUnits[slot] != unit) continue;

                Button button = slotButtons[slot];
                if (button == null) continue;

                Image portraitImage = button.GetComponentInChildren<Image>();
                if (portraitImage != null)
                    portraitImage.color = DeadColor;
            }
        }

        #endregion

        #region Camera Transitions

        private void OnSlotClicked(Unit targetUnit)
        {
            if (!CanTransitionCamera() || targetUnit == null) return;
            TransitionCameraToUnit(targetUnit);
        }

        private bool CanTransitionCamera()
        {
            if (isUIBlockedForAnimation() || isUIBlockedForMovement()) return false;
            if (combatManager != null)
            {
                var state = combatManager.currentState;
                return state == CombatState.WaitingForInput || state == CombatState.CameraTransition;
            }
            return true;
        }

        private void TransitionCameraToUnit(Unit targetUnit)
        {
            if (targetUnit?.currentTile == null) return;
            var cameraController = FindAnyObjectByType<CameraController>();
            if (cameraController == null) return;
            StartCoroutine(SmoothCameraTransition(cameraController, targetUnit));
        }

        private IEnumerator SmoothCameraTransition(CameraController cameraController, Unit targetUnit)
        {
            SetAllButtonsInteractable(false);
            yield return StartCoroutine(cameraController.TransitionTo(cameraController.UnitFocusPosition(targetUnit)));
            SetAllButtonsInteractable(true);
        }

        #endregion

        #region Button Interactability

        private void UpdateAllButtonInteractability()
        {
            bool interactable = CanTransitionCamera();
            foreach (var button in slotButtons)
            {
                if (button != null)
                    button.interactable = interactable;
            }
        }

        private void SetAllButtonsInteractable(bool interactable)
        {
            foreach (var button in slotButtons)
            {
                if (button != null)
                    button.interactable = interactable && CanTransitionCamera();
            }
        }

        #endregion
    }
}