using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Manages the turn-order indicator strip: creates indicator prefabs, updates
    /// active-turn highlighting, and handles camera-transition clicks.
    /// Attach to the same GameObject as UIManager.
    /// </summary>
    public class TurnOrderUIController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Turn Order UI")]
        [SerializeField] private Transform turnIndicatorsParent;
        [SerializeField] private GameObject turnIndicatorPrefab;

        #endregion

        #region Private Fields

        private TurnManager turnManager;
        private CombatManager combatManager;
        private List<GameObject> turnIndicatorObjects = new List<GameObject>();
        private int currentTurnIndex = -1;

        // Injected by UIManager so the controller knows when UI should be interactive
        private System.Func<bool> isUIBlockedForAnimation;
        private System.Func<bool> isUIBlockedForMovement;

        #endregion

        #region Initialization

        public void Initialize(
            TurnManager turnManager,
            CombatManager combatManager,
            System.Func<bool> isUIBlockedForAnimation,
            System.Func<bool> isUIBlockedForMovement)
        {
            this.turnManager = turnManager;
            this.combatManager = combatManager;
            this.isUIBlockedForAnimation = isUIBlockedForAnimation;
            this.isUIBlockedForMovement = isUIBlockedForMovement;
        }

        #endregion

        #region Public API — called by UIManager event handlers

        public void HandleTurnStarted(Unit unit)
        {
            if (turnIndicatorObjects.Count == 0)
                InitializeTurnOrderUI();
            else
                UpdateTurnIndicatorStates();
        }

        public void HandleTurnIndexChanged(int totalTurns, int currentIndex)
        {
            UpdateTurnIndicatorStates();
        }

        #endregion

        #region Turn Order UI

        private void InitializeTurnOrderUI()
        {
            if (turnManager == null || turnIndicatorsParent == null || turnIndicatorPrefab == null) return;

            ClearTurnIndicators();
            CreateTurnIndicators();
            UpdateTurnIndicatorStates();
        }

        private void CreateTurnIndicators()
        {
            var turnOrder = turnManager.TurnOrder;
            for (int i = 0; i < turnOrder.Count; i++)
            {
                var unit = turnOrder[i];
                GameObject indicatorObj = Instantiate(turnIndicatorPrefab, turnIndicatorsParent);
                SetupTurnIndicator(indicatorObj, unit, i);
                turnIndicatorObjects.Add(indicatorObj);
            }
        }

        private void SetupTurnIndicator(GameObject indicatorObj, Unit unit, int turnIndex)
        {
            Button button = indicatorObj.GetComponent<Button>();
            Image portraitImage = indicatorObj.transform.Find("Portrait")?.GetComponent<Image>();

            if (portraitImage != null && unit != null)
                SetUnitPortrait(portraitImage, unit);

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OnTurnIndicatorClicked(turnIndex, unit));
            }
        }

        private void SetUnitPortrait(Image portraitImage, Unit unit)
        {
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

        private void UpdateTurnIndicatorStates()
        {
            if (turnManager == null) return;
            currentTurnIndex = turnManager.CurrentTurnIndex;

            for (int i = 0; i < turnIndicatorObjects.Count; i++)
            {
                var indicatorObj = turnIndicatorObjects[i];
                if (indicatorObj != null)
                    UpdateSingleIndicatorState(indicatorObj, i == currentTurnIndex);
            }
        }

        private void UpdateSingleIndicatorState(GameObject indicatorObj, bool isActive)
        {
            Button button = indicatorObj.GetComponent<Button>();
            if (button != null)
                button.interactable = CanTransitionCamera();

            Image backgroundImage = indicatorObj.GetComponent<Image>();
            if (backgroundImage != null)
                backgroundImage.color = isActive ? Color.green : Color.white;

            Transform activeIndicator = indicatorObj.transform.Find("ActiveIndicator");
            if (activeIndicator != null)
                activeIndicator.gameObject.SetActive(isActive);
        }

        private void ClearTurnIndicators()
        {
            foreach (var indicatorObj in turnIndicatorObjects)
            {
                if (indicatorObj != null)
                    DestroyImmediate(indicatorObj);
            }
            turnIndicatorObjects.Clear();
        }

        private void SetTurnIndicatorButtonsInteractable(bool interactable)
        {
            foreach (var indicatorObj in turnIndicatorObjects)
            {
                if (indicatorObj == null) continue;
                Button button = indicatorObj.GetComponent<Button>();
                if (button != null)
                    button.interactable = interactable && CanTransitionCamera();
            }
        }

        #endregion

        #region Camera Transitions

        private void OnTurnIndicatorClicked(int turnIndex, Unit targetUnit)
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
            SetTurnIndicatorButtonsInteractable(false);
            yield return StartCoroutine(cameraController.TransitionTo(cameraController.UnitFocusPosition(targetUnit)));
            SetTurnIndicatorButtonsInteractable(true);
        }

        #endregion
    }
}