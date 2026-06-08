using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Manages the ability panel: expand/collapse, button sprites and interactability,
    /// and ability tooltips.
    /// Attach to the same GameObject as UIManager.
    /// </summary>
    public class AbilityPanelController : MonoBehaviour
    {
        #region Serialized Fields

        [Header("Ability Panel")]
        [SerializeField] private Button expandAbilitiesButton;
        [SerializeField] private GameObject abilitiesPanel;
        [SerializeField] private Button[] abilityButtons = new Button[3];
        [SerializeField] private TextMeshProUGUI abilityTooltipText;

        #endregion

        #region Public State

        public bool AreAbilitiesExpanded => abilitiesExpanded;

        #endregion

        #region Private Fields

        private CombatManager combatManager;
        private bool abilitiesExpanded = false;

        // Injected by UIManager
        private System.Func<bool> isUIBlockedForAnimation;
        private System.Func<bool> isUIBlockedForMovement;
        private System.Func<Unit> getCurrentPlayer;

        #endregion

        #region Initialization

        public void Initialize(
            CombatManager combatManager,
            System.Func<bool> isUIBlockedForAnimation,
            System.Func<bool> isUIBlockedForMovement,
            System.Func<Unit> getCurrentPlayer)
        {
            this.combatManager = combatManager;
            this.isUIBlockedForAnimation = isUIBlockedForAnimation;
            this.isUIBlockedForMovement = isUIBlockedForMovement;
            this.getCurrentPlayer = getCurrentPlayer;

            SetupButtonListeners();
            SetAbilitiesExpanded(false);
        }

        void OnDestroy()
        {
            CleanupButtonListeners();
        }

        private void SetupButtonListeners()
        {
            if (expandAbilitiesButton != null)
                expandAbilitiesButton.onClick.AddListener(ExpandAbilities);

            for (int i = 0; i < abilityButtons.Length; i++)
            {
                int slotIndex = i;
                if (abilityButtons[i] != null)
                    abilityButtons[i].onClick.AddListener(() => OnAbilityButtonClicked(slotIndex));
            }
        }

        private void CleanupButtonListeners()
        {
            if (expandAbilitiesButton != null)
                expandAbilitiesButton.onClick.RemoveAllListeners();
            foreach (var button in abilityButtons)
                if (button != null) button.onClick.RemoveAllListeners();
        }

        #endregion

        #region Public API — called by UIManager

        public void HandleTurnStarted()
        {
            if (abilitiesExpanded)
                SetAbilitiesExpanded(false);
        }

        public void UpdateAbilityDisplay()
        {
            var currentPlayer = getCurrentPlayer?.Invoke();
            if (currentPlayer?.characterData == null) return;

            var abilities = UnitLoadoutManager.GetAbilities(currentPlayer);

            if (expandAbilitiesButton != null)
            {
                bool hasAnyAbilities = System.Array.Exists(abilities, a => a != null);
                expandAbilitiesButton.interactable = hasAnyAbilities &&
                                                     !isUIBlockedForAnimation() &&
                                                     !isUIBlockedForMovement();
            }

            if (!abilitiesExpanded) return;

            for (int i = 0; i < abilityButtons.Length; i++)
            {
                if (abilityButtons[i] == null) continue;

                if (i < abilities.Length && abilities[i] != null)
                {
                    var ability = abilities[i];
                    Image buttonImage = abilityButtons[i].GetComponent<Image>();
                    if (buttonImage != null) buttonImage.sprite = ability.image;
                    abilityButtons[i].gameObject.SetActive(true);
                }
                else
                {
                    abilityButtons[i].gameObject.SetActive(false);
                }
            }

            HideAbilityTooltip();
        }

        public void UpdateAbilityButtonStates()
        {
            if (combatManager == null || getCurrentPlayer?.Invoke()?.characterData == null) return;

            bool canUseAbility = combatManager.CanUseAbility &&
                                 !isUIBlockedForAnimation() &&
                                 !isUIBlockedForMovement();

            if (expandAbilitiesButton != null)
                expandAbilitiesButton.interactable = canUseAbility;
        }

        public void CollapseIfExpanded()
        {
            if (abilitiesExpanded) CollapseAbilities();
        }

        public void ToggleAbilities()
        {
            if (isUIBlockedForAnimation() || isUIBlockedForMovement()) return;
            if (abilitiesExpanded) CollapseAbilities();
            else ExpandAbilities();
        }

        #endregion

        #region Expand / Collapse

        private void ExpandAbilities()
        {
            if (!abilitiesExpanded && !isUIBlockedForAnimation() && !isUIBlockedForMovement())
            {
                SetAbilitiesExpanded(true);
                UpdateAbilityDisplay();
                UpdateAbilityButtonStates();
            }
        }

        private void CollapseAbilities()
        {
            if (abilitiesExpanded)
            {
                SetAbilitiesExpanded(false);
                HideAbilityTooltip();
            }
        }

        private void SetAbilitiesExpanded(bool expanded)
        {
            abilitiesExpanded = expanded;
            if (expandAbilitiesButton != null) expandAbilitiesButton.gameObject.SetActive(!expanded);
            if (abilitiesPanel != null) abilitiesPanel.SetActive(expanded);
        }

        #endregion

        #region Button Handlers

        private void OnAbilityButtonClicked(int slotIndex)
        {
            var currentPlayer = getCurrentPlayer?.Invoke();
            if (combatManager == null || currentPlayer == null) return;
            if (!combatManager.CanUseAbility) return;

            var abilities = UnitLoadoutManager.GetAbilities(currentPlayer);
            if (slotIndex < 0 || slotIndex >= abilities.Length) return;
            if (abilities[slotIndex] == null) return;

            SetAbilitiesExpanded(false);
            combatManager.EnterAbilityTargeting(slotIndex);
        }

        #endregion

        #region Tooltips

        public void ShowAbilityTooltip(int slotIndex)
        {
            var currentPlayer = getCurrentPlayer?.Invoke();
            if (currentPlayer == null || abilityTooltipText == null) return;

            var abilities = UnitLoadoutManager.GetAbilities(currentPlayer);
            if (slotIndex < 0 || slotIndex >= abilities.Length || abilities[slotIndex] == null) return;

            var ability = abilities[slotIndex];
            abilityTooltipText.text = $"{ability.abilityName}\n{ability.description}\nDamage: {ability.damage}\nRange: {ability.range}";
        }

        public void HideAbilityTooltip()
        {
            if (abilityTooltipText != null)
                abilityTooltipText.text = "";
        }

        #endregion
    }
}