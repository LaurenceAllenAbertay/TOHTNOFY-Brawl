using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Drives the Debug Lobby scene.
    ///
    /// Layout:
    ///   Left panel  — one CharacterCard button per playable character.
    ///   Right panel — detail view for the selected character:
    ///                   portrait, name, include toggle, three ability dropdowns,
    ///                   passive dropdown.
    ///   Enemy strip — enemy count stepper + per-enemy character template dropdowns.
    ///   Footer      — validation label + Start Battle button.
    ///
    /// Ability dropdowns are populated from the selected character's
    /// CharacterData.availableAbilities pool — not a global flat list — so
    /// Kallper can only equip Kallper's abilities, etc.
    ///
    /// On Start Battle:
    ///   • Player loadouts are written into UnitLoadoutManager (persists across scenes).
    ///   • Spawn identities are written into DebugSessionConfig (static, survives load).
    ///   • The debug map scene is loaded; DebugMapSpawner reads both.
    /// </summary>
    public class DebugLobbyController : MonoBehaviour
    {
        // ── Inspector wiring ──────────────────────────────────────────────────

        [Header("Asset Lists (drag from Project window)")]
        [Tooltip("All CharacterData assets — one per playable character. Order determines card order.")]
        [SerializeField] private CharacterData[] allCharacters;

        [Tooltip("All enemy prefabs available to assign to enemy slots. " +
                 "Each prefab must have EnemyUnit + EnemyLoadout components. " +
                 "Multiple prefabs can share the same CharacterData but have different EnemyLoadout kits.")]
        [SerializeField] private GameObject[] allEnemyPrefabs;

        [Header("Left Panel — Character Cards")]
        [SerializeField] private Transform characterCardContainer;
        [SerializeField] private GameObject characterCardPrefab;

        [Header("Right Panel — Detail View")]
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private Image detailPortrait;
        [SerializeField] private TextMeshProUGUI detailNameText;
        [SerializeField] private Toggle includeToggle;
        [SerializeField] private TextMeshProUGUI includeLabel;

        [Header("Right Panel — Ability Slots")]
        [SerializeField] private TMP_Dropdown abilityDropdown0;
        [SerializeField] private TMP_Dropdown abilityDropdown1;
        [SerializeField] private TMP_Dropdown abilityDropdown2;

        [Header("Right Panel — Ability Descriptions")]
        [SerializeField] private TextMeshProUGUI abilityDescription0;
        [SerializeField] private TextMeshProUGUI abilityDescription1;
        [SerializeField] private TextMeshProUGUI abilityDescription2;

        [Header("Right Panel — Passive Slot")]
        [SerializeField] private TMP_Dropdown passiveDropdown;
        [SerializeField] private TextMeshProUGUI passiveDescription;

        [Header("Enemy Section")]
        [SerializeField] private TextMeshProUGUI enemyCountLabel;
        [SerializeField] private Button enemyCountDecButton;
        [SerializeField] private Button enemyCountIncButton;
        [SerializeField] private Transform enemyRowContainer;
        [SerializeField] private GameObject enemyRowPrefab;
        [SerializeField] private int maxEnemies = 6;

        [Header("Footer")]
        [SerializeField] private Button startBattleButton;
        [SerializeField] private TextMeshProUGUI validationLabel;

        [Header("Scene")]
        [Tooltip("Exact name of the debug map scene as it appears in Build Settings.")]
        [SerializeField] private string debugMapSceneName = "Debug";

        // ── Private state ─────────────────────────────────────────────────────

        private bool[]           _included;   // is this character ticked for the player team?
        private Ability[][]      _abilities;  // [charIndex][slot 0-2] — chosen abilities per character
        private PassiveAbility[] _passives;   // chosen passive per character

        private int _selectedCharIndex = -1;
        private int _enemyCount = 1;

        // Enemy slot choices — index into allEnemyPrefabs
        private readonly List<int> _enemyPrefabIndices = new List<int>();

        // Spawned card buttons for highlight management
        private readonly List<Button> _characterCardButtons = new List<Button>();
        private readonly List<Image>  _characterCardImages  = new List<Image>();

        // Spawned enemy row dropdowns
        private readonly List<TMP_Dropdown> _enemyDropdowns = new List<TMP_Dropdown>();

        // Enemy prefab options built once
        private List<TMP_Dropdown.OptionData> _enemyPrefabOptions;

        // ── Unity Lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (allCharacters == null || allCharacters.Length == 0)
            {
                Debug.LogError("[DebugLobby] allCharacters is empty — assign CharacterData assets in the Inspector.");
                return;
            }

            InitialiseState();
            BuildSharedDropdownOptions();
            BuildCharacterCards();
            SetupEnemySection();
            SetupFooter();

            detailPanel?.SetActive(false);
            RefreshEnemyRows();
            ValidateAndRefreshStartButton();
        }

        // ── State initialisation ──────────────────────────────────────────────

        private void InitialiseState()
        {
            int count = allCharacters.Length;
            _included  = new bool[count];
            _abilities = new Ability[count][];
            _passives  = new PassiveAbility[count];

            for (int i = 0; i < count; i++)
            {
                var cd = allCharacters[i];
                _included[i] = false;

                // Pre-fill ability slots with the first 3 available abilities for this
                // character so the lobby shows a meaningful default without the user
                // having to set every slot manually.
                _abilities[i] = new Ability[3];
                if (cd?.availableAbilities != null)
                {
                    for (int s = 0; s < 3 && s < cd.availableAbilities.Length; s++)
                        _abilities[i][s] = cd.availableAbilities[s];
                }

                // No passive default — start with None and let the user choose.
                _passives[i] = null;
            }

            _enemyCount = 1;
            _enemyPrefabIndices.Clear();
            _enemyPrefabIndices.Add(0);
        }

        // ── Shared dropdown option lists (passives, enemy prefabs) ────────────

        private void BuildSharedDropdownOptions()
        {
            // Enemy prefab options — shown in enemy row dropdowns
            _enemyPrefabOptions = new List<TMP_Dropdown.OptionData>();
            if (allEnemyPrefabs != null)
            {
                foreach (var prefab in allEnemyPrefabs)
                    if (prefab != null)
                        _enemyPrefabOptions.Add(new TMP_Dropdown.OptionData(prefab.name));
            }

            if (_enemyPrefabOptions.Count == 0)
                Debug.LogWarning("[DebugLobby] allEnemyPrefabs is empty — enemy rows will have no options.");
        }

        // ── Per-character ability option list ─────────────────────────────────

        /// <summary>
        /// Builds a dropdown option list from the selected character's availableAbilities pool.
        /// Called each time a new character card is selected, so the dropdowns always
        /// reflect that character's specific pool.
        /// </summary>
        private List<TMP_Dropdown.OptionData> BuildAbilityOptionsForCharacter(int charIndex)
        {
            var options = new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("— None —")
            };

            var cd = allCharacters[charIndex];
            if (cd?.availableAbilities != null)
            {
                foreach (var a in cd.availableAbilities)
                    if (a != null)
                        options.Add(new TMP_Dropdown.OptionData(a.abilityName));
            }

            return options;
        }

        /// <summary>
        /// Builds a dropdown option list from the selected character's availablePassives pool.
        /// Called each time a new character card is selected so the passive dropdown always
        /// reflects that character's specific pool — enforcing per-character passive restrictions.
        /// </summary>
        private List<TMP_Dropdown.OptionData> BuildPassiveOptionsForCharacter(int charIndex)
        {
            var options = new List<TMP_Dropdown.OptionData>
            {
                new TMP_Dropdown.OptionData("— None —")
            };

            var cd = allCharacters[charIndex];
            if (cd?.availablePassives != null)
            {
                foreach (var p in cd.availablePassives)
                    if (p != null)
                        options.Add(new TMP_Dropdown.OptionData(p.passiveName));
            }

            return options;
        }

        // ── Character Cards ───────────────────────────────────────────────────

        private void BuildCharacterCards()
        {
            if (characterCardContainer == null || characterCardPrefab == null) return;

            for (int i = 0; i < allCharacters.Length; i++)
            {
                int capturedIndex = i;
                var cd = allCharacters[i];

                var cardGO = Instantiate(characterCardPrefab, characterCardContainer);
                cardGO.name = $"Card_{cd?.characterName ?? i.ToString()}";

                var nameLabel = cardGO.GetComponentInChildren<TextMeshProUGUI>();
                if (nameLabel != null)
                    nameLabel.text = cd?.characterName ?? "Unknown";

                // portrait[0] = card background, portrait[1] = portrait image
                var portraits = cardGO.GetComponentsInChildren<Image>();
                if (portraits.Length > 1 && cd?.portrait != null)
                {
                    portraits[1].sprite = cd.portrait;
                    portraits[1].SetNativeSize();
                }

                // Button is on the root GameObject of the card prefab
                var btn = cardGO.GetComponent<Button>();
                if (btn != null)
                    btn.onClick.AddListener(() => OnCharacterCardClicked(capturedIndex));

                _characterCardButtons.Add(btn);
                _characterCardImages.Add(portraits.Length > 0 ? portraits[0] : null);
            }
        }

        private void OnCharacterCardClicked(int charIndex)
        {
            _selectedCharIndex = charIndex;
            RefreshDetailPanel();
            HighlightSelectedCard();
        }

        private void HighlightSelectedCard()
        {
            for (int i = 0; i < _characterCardImages.Count; i++)
            {
                var img = _characterCardImages[i];
                if (img == null) continue;
                img.color = (i == _selectedCharIndex)
                    ? new Color(0.35f, 0.75f, 1f)
                    : Color.white;
            }
        }

        // ── Detail Panel ──────────────────────────────────────────────────────

        private void RefreshDetailPanel()
        {
            if (_selectedCharIndex < 0 || _selectedCharIndex >= allCharacters.Length) return;
            detailPanel?.SetActive(true);

            var cd = allCharacters[_selectedCharIndex];

            if (detailPortrait != null && cd?.portrait != null)
                detailPortrait.sprite = cd.portrait;
            if (detailNameText != null)
                detailNameText.text = cd?.characterName ?? "Unknown";

            if (includeToggle != null)
            {
                includeToggle.onValueChanged.RemoveAllListeners();
                includeToggle.isOn = _included[_selectedCharIndex];
                includeToggle.onValueChanged.AddListener(OnIncludeToggleChanged);
                if (includeLabel != null)
                    includeLabel.text = _included[_selectedCharIndex] ? "In Team" : "Benched";
            }

            // Rebuild ability dropdowns using this character's own pool
            var abilityOptions = BuildAbilityOptionsForCharacter(_selectedCharIndex);
            RefreshAbilityDropdown(abilityDropdown0, 0, abilityOptions);
            RefreshAbilityDropdown(abilityDropdown1, 1, abilityOptions);
            RefreshAbilityDropdown(abilityDropdown2, 2, abilityOptions);

            // Sync description texts to match the current ability selections
            RefreshAbilityDescription(abilityDescription0, _abilities[_selectedCharIndex][0]);
            RefreshAbilityDescription(abilityDescription1, _abilities[_selectedCharIndex][1]);
            RefreshAbilityDescription(abilityDescription2, _abilities[_selectedCharIndex][2]);

            // Rebuild passive dropdown using this character's own pool
            var passiveOptions = BuildPassiveOptionsForCharacter(_selectedCharIndex);
            RefreshPassiveDropdown(passiveOptions);
            RefreshPassiveDescription(_passives[_selectedCharIndex]);
        }

        private void RefreshAbilityDropdown(TMP_Dropdown dropdown, int slot,
            List<TMP_Dropdown.OptionData> options)
        {
            if (dropdown == null) return;

            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.ClearOptions();
            dropdown.AddOptions(options);

            // Find the current ability in this character's availableAbilities pool.
            // Index 0 in the dropdown is "— None —", so pool index maps to dropdown index + 1.
            var current = _abilities[_selectedCharIndex][slot];
            int ddIndex = 0;
            var pool = allCharacters[_selectedCharIndex]?.availableAbilities;
            if (current != null && pool != null)
            {
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i] == current)
                    {
                        ddIndex = i + 1;
                        break;
                    }
                }
            }

            dropdown.value = ddIndex;
            dropdown.RefreshShownValue();

            int capturedSlot = slot;
            dropdown.onValueChanged.AddListener(val => OnAbilityDropdownChanged(capturedSlot, val));
        }

        private void RefreshPassiveDropdown(List<TMP_Dropdown.OptionData> options)
        {
            if (passiveDropdown == null) return;

            passiveDropdown.onValueChanged.RemoveAllListeners();
            passiveDropdown.ClearOptions();
            passiveDropdown.AddOptions(options);

            // Find the current passive in this character's availablePassives pool.
            // Index 0 in the dropdown is "— None —", so pool index maps to dropdown index + 1.
            var current = _passives[_selectedCharIndex];
            int ddIndex = 0;
            var pool = allCharacters[_selectedCharIndex]?.availablePassives;
            if (current != null && pool != null)
            {
                for (int i = 0; i < pool.Length; i++)
                {
                    if (pool[i] == current)
                    {
                        ddIndex = i + 1;
                        break;
                    }
                }
            }

            passiveDropdown.value = ddIndex;
            passiveDropdown.RefreshShownValue();
            passiveDropdown.onValueChanged.AddListener(OnPassiveDropdownChanged);
        }

        // ── Detail Panel Callbacks ────────────────────────────────────────────

        private void OnIncludeToggleChanged(bool isOn)
        {
            if (_selectedCharIndex < 0) return;
            _included[_selectedCharIndex] = isOn;
            if (includeLabel != null)
                includeLabel.text = isOn ? "In Team" : "Benched";
            ValidateAndRefreshStartButton();
        }

        private void OnAbilityDropdownChanged(int slot, int dropdownValue)
        {
            if (_selectedCharIndex < 0) return;
            // dropdownValue 0 → None; 1+ → availableAbilities[dropdownValue - 1]
            var pool = allCharacters[_selectedCharIndex]?.availableAbilities;
            var selected = (dropdownValue == 0 || pool == null) ? null : pool[dropdownValue - 1];
            _abilities[_selectedCharIndex][slot] = selected;

            // Update the matching description text immediately
            var descriptionLabel = slot == 0 ? abilityDescription0
                                 : slot == 1 ? abilityDescription1
                                 : abilityDescription2;
            RefreshAbilityDescription(descriptionLabel, selected);
        }

        private void OnPassiveDropdownChanged(int dropdownValue)
        {
            if (_selectedCharIndex < 0) return;
            var pool = allCharacters[_selectedCharIndex]?.availablePassives;
            var selected = (dropdownValue == 0 || pool == null) ? null : pool[dropdownValue - 1];
            _passives[_selectedCharIndex] = selected;
            RefreshPassiveDescription(selected);
        }

        // ── Description helpers ───────────────────────────────────────────────

        private void RefreshAbilityDescription(TextMeshProUGUI label, Ability ability)
        {
            if (label == null) return;
            label.text = ability != null ? ability.description : "";
        }

        private void RefreshPassiveDescription(PassiveAbility passive)
        {
            if (passiveDescription == null) return;
            passiveDescription.text = passive != null ? passive.description : "";
        }

        // ── Enemy Section ─────────────────────────────────────────────────────

        private void SetupEnemySection()
        {
            if (enemyCountDecButton != null)
                enemyCountDecButton.onClick.AddListener(OnEnemyCountDec);
            if (enemyCountIncButton != null)
                enemyCountIncButton.onClick.AddListener(OnEnemyCountInc);
        }

        private void OnEnemyCountDec()
        {
            if (_enemyCount <= 1) return;
            _enemyCount--;
            _enemyPrefabIndices.RemoveAt(_enemyPrefabIndices.Count - 1);
            RefreshEnemyRows();
            ValidateAndRefreshStartButton();
        }

        private void OnEnemyCountInc()
        {
            if (_enemyCount >= maxEnemies) return;
            _enemyCount++;
            _enemyPrefabIndices.Add(0);
            RefreshEnemyRows();
            ValidateAndRefreshStartButton();
        }

        private void RefreshEnemyRows()
        {
            if (enemyRowContainer == null || enemyRowPrefab == null) return;

            foreach (var dd in _enemyDropdowns)
                if (dd != null) Destroy(dd.transform.parent.gameObject);
            _enemyDropdowns.Clear();

            for (int i = 0; i < _enemyCount; i++)
            {
                int capturedIndex = i;
                var rowGO = Instantiate(enemyRowPrefab, enemyRowContainer);
                rowGO.name = $"EnemyRow_{i}";

                var labels = rowGO.GetComponentsInChildren<TextMeshProUGUI>();
                if (labels.Length > 0)
                    labels[0].text = $"Enemy {i + 1}";

                var dd = rowGO.GetComponentInChildren<TMP_Dropdown>();
                if (dd != null)
                {
                    dd.ClearOptions();
                    dd.AddOptions(_enemyPrefabOptions);
                    dd.value = _enemyPrefabIndices[i];
                    dd.RefreshShownValue();
                    dd.onValueChanged.AddListener(val => OnEnemyPrefabChanged(capturedIndex, val));
                }

                _enemyDropdowns.Add(dd);
            }

            if (enemyCountLabel != null)
                enemyCountLabel.text = _enemyCount.ToString();
        }

        private void OnEnemyPrefabChanged(int enemyIndex, int prefabIndex)
        {
            if (enemyIndex < _enemyPrefabIndices.Count)
                _enemyPrefabIndices[enemyIndex] = prefabIndex;
        }

        // ── Footer ────────────────────────────────────────────────────────────

        private void SetupFooter()
        {
            if (startBattleButton != null)
                startBattleButton.onClick.AddListener(OnStartBattleClicked);
        }

        private void ValidateAndRefreshStartButton()
        {
            bool anyPlayerIncluded = false;
            for (int i = 0; i < allCharacters.Length; i++)
                if (_included[i]) { anyPlayerIncluded = true; break; }

            bool valid = anyPlayerIncluded && _enemyCount > 0;

            if (startBattleButton != null)
                startBattleButton.interactable = valid;

            if (validationLabel != null)
            {
                validationLabel.text = valid
                    ? ""
                    : (!anyPlayerIncluded
                        ? "Select at least one player character."
                        : "Add at least one enemy.");
                validationLabel.gameObject.SetActive(!valid);
            }
        }

        private void OnStartBattleClicked()
        {
            if (UnitLoadoutManager.Instance == null)
            {
                Debug.LogError("[DebugLobby] UnitLoadoutManager not found in scene. " +
                               "Add a GameObject with UnitLoadoutManager to a persistent scene.");
                return;
            }

            DebugSessionConfig.Clear();

            // Write player loadouts into UnitLoadoutManager and spawn identities into config
            for (int i = 0; i < allCharacters.Length; i++)
            {
                if (!_included[i]) continue;

                var cd = allCharacters[i];

                // Manager persists across scenes — all combat systems read from here
                UnitLoadoutManager.Instance.SetPlayerLoadout(
                    cd,
                    (Ability[])_abilities[i].Clone(),
                    _passives[i]);

                DebugSessionConfig.PlayerSpawns.Add(new PlayerSpawnConfig
                {
                    characterData = cd
                });
            }

            // Write enemy spawn identities — abilities come from EnemyLoadout on the prefab
            foreach (int prefabIndex in _enemyPrefabIndices)
            {
                if (allEnemyPrefabs == null || prefabIndex >= allEnemyPrefabs.Length) continue;

                var prefab = allEnemyPrefabs[prefabIndex];
                if (prefab == null) continue;

                // Read the CharacterData from the prefab's Unit component so the config
                // carries it without needing a separate character selection per enemy slot.
                var unitComponent = prefab.GetComponent<Unit>();
                if (unitComponent == null)
                {
                    Debug.LogWarning($"[DebugLobby] Enemy prefab '{prefab.name}' has no Unit component — skipped.");
                    continue;
                }

                DebugSessionConfig.EnemySpawns.Add(new EnemySpawnConfig
                {
                    characterData = unitComponent.characterData,
                    prefab        = prefab
                });
            }

            SceneManager.LoadScene(debugMapSceneName);
        }
    }
}