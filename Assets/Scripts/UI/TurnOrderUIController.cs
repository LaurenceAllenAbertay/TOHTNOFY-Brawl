using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Manages the eight fixed turn-indicator buttons in the HUD.
    ///
    /// Slot 0 = current unit, Slots 1-7 = next seven units (wraps around the turn order).
    /// Each slot shows the unit's portrait and pans the camera to that unit on click.
    /// When a unit dies their portrait is tinted grey.
    ///
    /// Turn transitions play a staggered wave animation: a white flash rolls across all slots
    /// from last to first (hide phase), sprites are swapped at the midpoint, then the wave
    /// rolls back across revealing the new portraits (reveal phase).
    ///
    /// Each button must have exactly two child Images in this order:
    ///   [0] PortraitImage  — the unit portrait
    ///   [1] OverlayImage   — solid white, alpha 0 at rest, Raycast Target OFF
    ///
    /// The eight Button GameObjects are pre-placed in the scene and assigned via the Inspector.
    /// Attach to the same GameObject as UIManager.
    /// </summary>
    public class TurnOrderUIController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Turn Indicator Buttons (Slot 0 = Current, 1-7 = Upcoming)")]
        [SerializeField] private Button slot0Button;
        [SerializeField] private Button slot1Button;
        [SerializeField] private Button slot2Button;
        [SerializeField] private Button slot3Button;
        [SerializeField] private Button slot4Button;
        [SerializeField] private Button slot5Button;
        [SerializeField] private Button slot6Button;
        [SerializeField] private Button slot7Button;

        [Header("Transition Timing")]
        [SerializeField] [Range(0.02f, 0.5f)] private float fadeOutDuration  = 0.08f;
        [SerializeField] [Range(0.02f, 0.5f)] private float fadeInDuration   = 0.12f;
        [SerializeField] [Range(0.01f, 0.3f)] private float slotStaggerDelay = 0.04f;

        #endregion

        #region Private Fields

        private TurnManager turnManager;
        private CombatManager combatManager;

        // Injected by UIManager so the controller knows when UI should be interactive
        private System.Func<bool> isUIBlockedForAnimation;
        private System.Func<bool> isUIBlockedForMovement;

        // Populated in Initialize() from the serialized button fields
        private Button[] slotButtons;

        // Explicit portrait and overlay references — one per slot, indexed to match slotButtons.
        // Stored directly so we never rely on GetComponentInChildren ordering at runtime.
        private Image[] portraitImages;
        private Image[] overlayImages;

        // Tracks which unit is currently assigned to each slot so we can grey out
        // the correct portrait when UnitManager.OnUnitDied fires
        private Unit[] slotUnits;

        // Prevents the transition animation from running on the very first population
        private bool hasPopulatedOnce = false;

        // Ensures only one transition coroutine runs at a time
        private Coroutine transitionCoroutine;

        private static readonly Color AliveColor     = Color.white;
        private static readonly Color DeadColor      = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color OverlayHidden  = new Color(1f, 1f, 1f, 0f);
        private static readonly Color OverlayVisible = new Color(1f, 1f, 1f, 1f);

        #endregion

        #region Initialization

        public void Initialize(
            TurnManager turnManager,
            CombatManager combatManager,
            System.Func<bool> isUIBlockedForAnimation,
            System.Func<bool> isUIBlockedForMovement)
        {
            this.turnManager             = turnManager;
            this.combatManager           = combatManager;
            this.isUIBlockedForAnimation = isUIBlockedForAnimation;
            this.isUIBlockedForMovement  = isUIBlockedForMovement;

            slotButtons = new Button[]
            {
                slot0Button, slot1Button, slot2Button, slot3Button,
                slot4Button, slot5Button, slot6Button, slot7Button
            };

            portraitImages = new Image[8];
            overlayImages  = new Image[8];
            slotUnits      = new Unit[8];

            for (int i = 0; i < 8; i++)
            {
                if (slotButtons[i] == null) continue;

                Image[] children = slotButtons[i].GetComponentsInChildren<Image>();

                // children[0] = PortraitImage, children[1] = OverlayImage
                // This matches the hierarchy order set up in the scene.
                if (children.Length >= 2)
                {
                    portraitImages[i] = children[0];
                    overlayImages[i]  = children[1];

                    // Ensure overlay starts fully transparent
                    overlayImages[i].color = OverlayHidden;
                }
                else
                {
                    Debug.LogWarning($"[TurnOrderUIController] Slot {i} button does not have " +
                                     $"two child Images (PortraitImage + OverlayImage). " +
                                     $"Check the hierarchy.");
                    if (children.Length >= 1)
                        portraitImages[i] = children[0];
                }
            }

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
        /// On the first call, populates all slots instantly with no animation.
        /// On subsequent calls, kicks off the cascade transition coroutine.
        /// </summary>
        private void RefreshAllSlots()
        {
            if (turnManager == null) return;

            if (!hasPopulatedOnce)
            {
                PopulateSlotsImmediate();
                hasPopulatedOnce = true;
                return;
            }

            // Cancel any in-progress transition before starting a new one
            if (transitionCoroutine != null)
                StopCoroutine(transitionCoroutine);

            transitionCoroutine = StartCoroutine(TransitionCoroutine());
        }

        /// <summary>
        /// Instantly assigns portraits to all slots with no animation. Used on first populate.
        /// </summary>
        private void PopulateSlotsImmediate()
        {
            IReadOnlyList<Unit> order = turnManager.TurnOrder;
            int count        = order.Count;
            int currentIndex = turnManager.CurrentTurnIndex;

            for (int slot = 0; slot < 8; slot++)
            {
                if (slotButtons[slot] == null) continue;

                int unitIndex   = (currentIndex + slot) % count;
                Unit unit       = order[unitIndex];
                slotUnits[slot] = unit;

                ApplyPortrait(slot, unit);
                ApplyTint(slot, unit);
                SetSlotClickListener(slotButtons[slot], unit);

                if (overlayImages[slot] != null)
                    overlayImages[slot].color = OverlayHidden;
            }

            UpdateAllButtonInteractability();
        }

        /// <summary>
        /// Full staggered wave transition:
        ///   1. All 8 overlays fade to white in parallel, each starting slotStaggerDelay after
        ///      the previous (slot 7 first, slot 0 last). Waits until the last overlay is white.
        ///   2. Sprites are swapped at the midpoint while everything is white.
        ///   3. All 8 overlays fade back out in parallel with the same stagger, revealing
        ///      the new portraits underneath.
        /// Buttons are non-interactable for the duration.
        /// </summary>
        private IEnumerator TransitionCoroutine()
        {
            SetAllButtonsInteractable(false);

            // --- PHASE 1: White wave rolling from slot 7 to slot 0 ---
            // All fades start in parallel, each offset by slotStaggerDelay from the last.
            // Slot 7 starts immediately (delay 0), slot 0 starts last (delay 7 * stagger).
            for (int slot = 7; slot >= 0; slot--)
            {
                if (overlayImages[slot] != null)
                    StartCoroutine(StaggeredFadeIn(slot, (7 - slot) * slotStaggerDelay));
            }

            // Wait for the last slot (slot 0, longest delay) to finish fading fully white
            yield return new WaitForSeconds((7 * slotStaggerDelay) + fadeOutDuration);

            // --- MIDPOINT: Swap all portraits while everything is white ---
            IReadOnlyList<Unit> order = turnManager.TurnOrder;
            int count        = order.Count;
            int currentIndex = turnManager.CurrentTurnIndex;

            for (int slot = 0; slot < 8; slot++)
            {
                if (slotButtons[slot] == null) continue;

                int unitIndex   = (currentIndex + slot) % count;
                Unit unit       = order[unitIndex];
                slotUnits[slot] = unit;

                ApplyPortrait(slot, unit);
                ApplyTint(slot, unit);
                SetSlotClickListener(slotButtons[slot], unit);
            }

            // --- PHASE 2: White wave clearing from slot 7 to slot 0 ---
            // Same stagger pattern — each overlay fades out revealing the new portrait.
            for (int slot = 7; slot >= 0; slot--)
            {
                if (overlayImages[slot] != null)
                    StartCoroutine(StaggeredFadeOut(slot, (7 - slot) * slotStaggerDelay));
            }

            // Wait for the last slot (slot 0) to finish fading out
            yield return new WaitForSeconds((7 * slotStaggerDelay) + fadeInDuration);

            UpdateAllButtonInteractability();
            transitionCoroutine = null;
        }

        /// <summary>
        /// Waits for the given delay then fades the overlay in to fully white and holds it there.
        /// </summary>
        private IEnumerator StaggeredFadeIn(int slot, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return StartCoroutine(FadeOverlay(slot, OverlayHidden, OverlayVisible, fadeOutDuration));
        }

        /// <summary>
        /// Waits for the given delay then fades the overlay out from white to transparent.
        /// </summary>
        private IEnumerator StaggeredFadeOut(int slot, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            overlayImages[slot].color = OverlayVisible;
            yield return StartCoroutine(FadeOverlay(slot, OverlayVisible, OverlayHidden, fadeInDuration));
        }

        /// <summary>
        /// Smoothly lerps a single slot's overlay between two colors over the given duration.
        /// </summary>
        private IEnumerator FadeOverlay(int slot, Color from, Color to, float duration)
        {
            Image overlay = overlayImages[slot];
            if (overlay == null) yield break;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                overlay.color = Color.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            overlay.color = to;
        }

        #endregion

        #region Slot Helpers

        /// <summary>
        /// Assigns the unit's portrait sprite to the portrait Image for this slot.
        /// Falls back to the unit's SpriteRenderer sprite if no portrait is set on CharacterData.
        /// </summary>
        private void ApplyPortrait(int slot, Unit unit)
        {
            Image portrait = portraitImages[slot];
            if (portrait == null || unit == null) return;

            if (unit.characterData != null && unit.characterData.portrait != null)
            {
                portrait.sprite = unit.characterData.portrait;
            }
            else
            {
                var sr = unit.GetComponentInChildren<SpriteRenderer>();
                if (sr != null && sr.sprite != null)
                    portrait.sprite = sr.sprite;
            }
        }

        /// <summary>
        /// Tints the portrait grey if the unit is dead, white if alive.
        /// </summary>
        private void ApplyTint(int slot, Unit unit)
        {
            Image portrait = portraitImages[slot];
            if (portrait == null) return;
            portrait.color = (unit != null && unit.IsDead) ? DeadColor : AliveColor;
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
                ApplyTint(slot, unit);
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