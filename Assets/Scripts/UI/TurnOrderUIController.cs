using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    public class TurnOrderUIController : MonoBehaviour
    {
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
        
        private TurnManager turnManager;
        private CombatManager combatManager;
        
        private System.Func<bool> isUIBlockedForAnimation;
        private System.Func<bool> isUIBlockedForMovement;

        private Button[] slotButtons;

        private Image[] portraitImages;
        private Image[] overlayImages;

        private Unit[] slotUnits;

        private bool hasPopulatedOnce = false;

        private Coroutine transitionCoroutine;

        private static readonly Color AliveColor     = Color.white;
        private static readonly Color DeadColor      = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color OverlayHidden  = new Color(1f, 1f, 1f, 0f);
        private static readonly Color OverlayVisible = new Color(1f, 1f, 1f, 1f);


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
                
                if (children.Length >= 2)
                {
                    portraitImages[i] = children[0];
                    overlayImages[i]  = children[1];
                    
                    overlayImages[i].color = OverlayHidden;
                }
                else
                {
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

        public void HandleTurnStarted(Unit unit)
        {
            RefreshAllSlots();
        }

        public void HandleTurnIndexChanged(int totalTurns, int currentIndex)
        {
            RefreshAllSlots();
        }

        public void HandleCombatEmpty()
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }

            for (int slot = 0; slot < slotUnits.Length; slot++)
            {
                slotUnits[slot] = null;

                if (portraitImages[slot] != null)
                    portraitImages[slot].sprite = null;

                if (overlayImages[slot] != null)
                    overlayImages[slot].color = OverlayHidden;

                if (slotButtons[slot] != null)
                    slotButtons[slot].onClick.RemoveAllListeners();
            }

            SetAllButtonsInteractable(false);
        }

        
        private void RefreshAllSlots()
        {
            if (turnManager == null) return;

            if (!hasPopulatedOnce)
            {
                PopulateSlotsImmediate();
                hasPopulatedOnce = true;
                return;
            }
            
            if (transitionCoroutine != null)
                StopCoroutine(transitionCoroutine);

            transitionCoroutine = StartCoroutine(TransitionCoroutine());
        }
        
        private void PopulateSlotsImmediate()
        {
            IReadOnlyList<Unit> order = turnManager.TurnOrder;
            int count        = order.Count;
            int currentIndex = turnManager.CurrentTurnIndex;

            if (count == 0) return;

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

        private IEnumerator TransitionCoroutine()
        {
            SetAllButtonsInteractable(false);
            
            for (int slot = 7; slot >= 0; slot--)
            {
                if (overlayImages[slot] != null)
                    StartCoroutine(StaggeredFadeIn(slot, (7 - slot) * slotStaggerDelay));
            }
            
            yield return new WaitForSeconds((7 * slotStaggerDelay) + fadeOutDuration);

            IReadOnlyList<Unit> order = turnManager.TurnOrder;
            int count        = order.Count;
            int currentIndex = turnManager.CurrentTurnIndex;

            if (count == 0)
            {
                transitionCoroutine = null;
                yield break;
            }

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

            for (int slot = 7; slot >= 0; slot--)
            {
                if (overlayImages[slot] != null)
                    StartCoroutine(StaggeredFadeOut(slot, (7 - slot) * slotStaggerDelay));
            }
            
            yield return new WaitForSeconds((7 * slotStaggerDelay) + fadeInDuration);

            UpdateAllButtonInteractability();
            transitionCoroutine = null;
        }

        private IEnumerator StaggeredFadeIn(int slot, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            yield return StartCoroutine(FadeOverlay(slot, OverlayHidden, OverlayVisible, fadeOutDuration));
        }

        private IEnumerator StaggeredFadeOut(int slot, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            overlayImages[slot].color = OverlayVisible;
            yield return StartCoroutine(FadeOverlay(slot, OverlayVisible, OverlayHidden, fadeInDuration));
        }
        
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

        private void ApplyTint(int slot, Unit unit)
        {
            Image portrait = portraitImages[slot];
            if (portrait == null) return;
            portrait.color = (unit != null && unit.IsDead) ? DeadColor : AliveColor;
        }

        private void SetSlotClickListener(Button button, Unit unit)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => OnSlotClicked(unit));
        }
        
        private void HandleUnitDied(Unit unit)
        {
            for (int slot = 0; slot < slotUnits.Length; slot++)
            {
                if (slotUnits[slot] != unit) continue;
                ApplyTint(slot, unit);
            }
        }

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
    }
}