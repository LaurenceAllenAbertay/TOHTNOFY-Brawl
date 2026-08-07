using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace DDD.TNFY.BRAWL
{
    public class AdminConsoleController : MonoBehaviour
    {
        [SerializeField] private GameObject consoleRoot;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private TextMeshProUGUI feedbackText;
        [SerializeField] private ScrollRect scrollRect;

        [SerializeField] private int maxScrollbackLines = 50;

        private readonly List<string> _scrollback = new List<string>();

        private bool _isOpen;
        private AdminParsedCommand _pendingCommand;
        private bool _pendingRunning;

        private void Awake()
        {
            if (consoleRoot != null)
                consoleRoot.SetActive(false);

            if (inputField != null)
                inputField.onSubmit.AddListener(OnSubmit);
        }

        private void OnEnable()
        {
            InputManager.OnConsoleTogglePressed += HandleTogglePressed;
            InputManager.OnConsumedClick += HandleTargetClicked;
        }

        private void OnDisable()
        {
            InputManager.OnConsoleTogglePressed -= HandleTogglePressed;
            InputManager.OnConsumedClick -= HandleTargetClicked;

            if (_isOpen)
                Close();
        }

        private void HandleTogglePressed()
        {
            if (_isOpen)
                Close();
            else
                Open();
        }

        private void Open()
        {
            _isOpen = true;

            InputManager.SetTileClickConsumer(() => _pendingCommand != null);

            if (consoleRoot != null)
                consoleRoot.SetActive(true);

            ScrollToBottom();
            FocusInputField();
        }

        private void Close()
        {
            _isOpen = false;

            if (_pendingCommand != null)
            {
                Log($"Cancelled pending '{_pendingCommand.commandType}' — console closed before target was selected.");
                _pendingCommand = null;
            }

            InputManager.SetTileClickConsumer(null);
            InputManager.SetGameplayKeysEnabled(true);

            if (inputField != null)
                inputField.interactable = true;

            if (consoleRoot != null)
                consoleRoot.SetActive(false);
        }

        private void FocusInputField()
        {
            InputManager.SetGameplayKeysEnabled(false);
            if (CameraController.Instance != null)
                CameraController.Instance.ClearMovementInput();

            if (inputField == null) return;

            inputField.interactable = true;
            inputField.text = string.Empty;
            inputField.ActivateInputField();
            inputField.Select();
        }

        private void LockInputFieldForTargeting()
        {
            if (inputField == null) return;

            inputField.DeactivateInputField();
            inputField.interactable = false;
        }

        private void OnSubmit(string rawInput)
        {
            if (!_isOpen) return;
            if (_pendingRunning) return;
            if (string.IsNullOrWhiteSpace(rawInput))
            {
                FocusInputField();
                return;
            }

            var parseResult = AdminCommandParser.Parse(rawInput);
            if (!parseResult.success)
            {
                Log($"> {rawInput}");
                Log(parseResult.errorMessage);
                FocusInputField();
                return;
            }

            Log($"> {rawInput}");
            Dispatch(parseResult.command);
        }

        private void Dispatch(AdminParsedCommand command)
        {
            if (command.commandType == AdminCommandType.Help)
            {
                Log(AdminCommandHelp.GetHelpText(command.positionalArg));
                FocusInputField();
                return;
            }

            if (CombatManager.Instance == null || !CombatManager.Instance.IsSafeForAdminCommand)
            {
                Log("Error: cannot run admin commands right now (not your turn, or something is mid-action).");
                FocusInputField();
                return;
            }

            if (command.commandType == AdminCommandType.Reset)
            {
                Log(AdminCommandExecutor.ExecuteReset());
                return;
            }

            if (command.commandType == AdminCommandType.Clear)
            {
                RunSync(AdminCommandExecutor.ExecuteClear(command));
                return;
            }

            switch (command.commandType)
            {
                case AdminCommandType.Spawn:
                case AdminCommandType.Teleport:
                case AdminCommandType.Kill:
                case AdminCommandType.Delete:
                case AdminCommandType.Heal:
                case AdminCommandType.Damage:
                case AdminCommandType.TrueDamage:
                case AdminCommandType.StatusEffect:
                case AdminCommandType.Skip:
                    ArmForTarget(command);
                    break;

                default:
                    Log($"Error: '{command.commandType}' is not yet wired to a target flow.");
                    FocusInputField();
                    break;
            }
        }

        private void ArmForTarget(AdminParsedCommand command)
        {
            _pendingCommand = command;
            LockInputFieldForTargeting();
            InputManager.SetGameplayKeysEnabled(true);
            Log(TargetPromptFor(command.commandType));
        }

        private string TargetPromptFor(AdminCommandType type)
        {
            switch (type)
            {
                case AdminCommandType.Spawn:
                case AdminCommandType.Teleport:
                    return "Click a tile to target...";
                default:
                    return "Click a unit to target...";
            }
        }

        private void HandleTargetClicked(Tile tile, Unit clickedUnit)
        {
            if (_pendingCommand == null) return;

            var command = _pendingCommand;
            _pendingCommand = null;

            if (CombatManager.Instance == null || !CombatManager.Instance.IsSafeForAdminCommand)
            {
                Log("Error: cannot resolve target — state changed since the command was entered. Try again.");
                FocusInputField();
                return;
            }

            switch (command.commandType)
            {
                case AdminCommandType.Spawn:
                    RunSpawn(command, tile);
                    return;

                case AdminCommandType.Teleport:
                    RunTeleport(command, tile);
                    return;
            }

            Unit targetUnit = clickedUnit != null ? clickedUnit : (tile != null ? tile.currentUnit : null);

            if (targetUnit == null)
            {
                _pendingCommand = command;
                Log("No unit there — click directly on a unit.");
                return;
            }

            switch (command.commandType)
            {
                case AdminCommandType.Kill:
                    RunKill(targetUnit);
                    break;

                case AdminCommandType.Delete:
                    RunSync(AdminCommandExecutor.ExecuteDelete(targetUnit));
                    break;

                case AdminCommandType.Heal:
                    RunSync(AdminCommandExecutor.ExecuteHeal(command, targetUnit));
                    break;

                case AdminCommandType.Damage:
                    RunSync(AdminCommandExecutor.ExecuteDamage(command, targetUnit));
                    break;

                case AdminCommandType.TrueDamage:
                    RunSync(AdminCommandExecutor.ExecuteTrueDamage(command, targetUnit));
                    break;

                case AdminCommandType.StatusEffect:
                    RunSync(AdminCommandExecutor.ExecuteStatusEffect(command, targetUnit));
                    break;

                case AdminCommandType.Skip:
                    RunSkip(targetUnit);
                    break;
            }
        }

        private void RunTeleport(AdminParsedCommand command, Tile tile)
        {
            string result = AdminCommandExecutor.ExecuteTeleport(
                TurnManager.Instance != null ? TurnManager.Instance.CurrentUnit : null, tile);

            if (result == null)
            {
                _pendingCommand = command;
                Log("Tile is occupied or impassable — click a different tile.");
                return;
            }

            RunSync(result);
        }

        private void RunSync(string resultMessage)
        {
            Log(resultMessage);
            FocusInputField();
        }

        private void RunSpawn(AdminParsedCommand command, Tile tile)
        {
            _pendingRunning = true;
            StartCoroutine(RunSpawnCoroutine(command, tile));
        }

        private System.Collections.IEnumerator RunSpawnCoroutine(AdminParsedCommand command, Tile tile)
        {
            yield return StartCoroutine(AdminCommandExecutor.ExecuteSpawn(command, tile, message =>
            {
                Log(message);
            }));

            _pendingRunning = false;
            FocusInputField();
        }

        private void RunKill(Unit targetUnit)
        {
            _pendingRunning = true;
            StartCoroutine(RunKillCoroutine(targetUnit));
        }

        private System.Collections.IEnumerator RunKillCoroutine(Unit targetUnit)
        {
            yield return StartCoroutine(AdminCommandExecutor.ExecuteKill(targetUnit, message =>
            {
                Log(message);
            }));

            _pendingRunning = false;
            FocusInputField();
        }

        private void RunSkip(Unit targetUnit)
        {
            _pendingRunning = true;
            StartCoroutine(RunSkipCoroutine(targetUnit));
        }

        private System.Collections.IEnumerator RunSkipCoroutine(Unit targetUnit)
        {
            yield return StartCoroutine(AdminCommandExecutor.ExecuteSkip(targetUnit, message =>
            {
                Log(message);
            }));

            _pendingRunning = false;
            FocusInputField();
        }

        private void Log(string message)
        {
            _scrollback.Add(message);
            while (_scrollback.Count > maxScrollbackLines)
                _scrollback.RemoveAt(0);

            if (feedbackText != null)
                feedbackText.text = string.Join("\n", _scrollback);

            ScrollToBottom();
        }

        private void ScrollToBottom()
        {
            if (scrollRect == null) return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}