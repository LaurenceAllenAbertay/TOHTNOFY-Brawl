using System.Collections.Generic;
using System.Linq;

namespace DDD.TNFY.BRAWL
{
    public enum AdminCommandType
    {
        Spawn,
        Kill,
        Delete,
        Heal,
        Damage,
        TrueDamage,
        StatusEffect,
        Teleport,
        Skip,
        Reset,
        Help
    }

    public class AdminParsedCommand
    {
        public AdminCommandType commandType;
        public string positionalArg;
        public Dictionary<string, List<string>> keyedArgs = new Dictionary<string, List<string>>();

        public bool HasKey(string key) => keyedArgs.ContainsKey(key);

        public List<string> GetValues(string key) =>
            keyedArgs.TryGetValue(key, out var values) ? values : new List<string>();

        public string GetSingleValue(string key) =>
            GetValues(key).FirstOrDefault();

        public string ResolvePositional(string keyName)
        {
            if (!string.IsNullOrEmpty(positionalArg))
                return positionalArg;

            return GetSingleValue(keyName);
        }
    }

    public class AdminCommandParseResult
    {
        public bool success;
        public AdminParsedCommand command;
        public string errorMessage;

        public static AdminCommandParseResult Ok(AdminParsedCommand command) =>
            new AdminCommandParseResult { success = true, command = command };

        public static AdminCommandParseResult Fail(string message) =>
            new AdminCommandParseResult { success = false, errorMessage = message };
    }

    public static class AdminCommandParser
    {
        private static readonly Dictionary<string, AdminCommandType> CommandNames =
            new Dictionary<string, AdminCommandType>
            {
                { "spawn",        AdminCommandType.Spawn },
                { "kill",         AdminCommandType.Kill },
                { "delete",       AdminCommandType.Delete },
                { "heal",         AdminCommandType.Heal },
                { "damage",       AdminCommandType.Damage },
                { "truedamage",   AdminCommandType.TrueDamage },
                { "statuseffect", AdminCommandType.StatusEffect },
                { "teleport",     AdminCommandType.Teleport },
                { "skip",         AdminCommandType.Skip },
                { "reset",        AdminCommandType.Reset },
                { "help",         AdminCommandType.Help },
            };

        public static AdminCommandParseResult Parse(string rawInput)
        {
            if (string.IsNullOrWhiteSpace(rawInput))
                return AdminCommandParseResult.Fail("Empty command.");

            string trimmed = rawInput.Trim();
            if (!trimmed.StartsWith("/"))
                return AdminCommandParseResult.Fail("Commands must start with '/'.");

            int firstSpace = trimmed.IndexOf(' ');
            string commandWord = firstSpace < 0 ? trimmed.Substring(1) : trimmed.Substring(1, firstSpace - 1);
            string remainder = firstSpace < 0 ? string.Empty : trimmed.Substring(firstSpace + 1).Trim();

            if (!CommandNames.TryGetValue(commandWord.ToLowerInvariant(), out var commandType))
                return AdminCommandParseResult.Fail($"Unknown command '/{commandWord}'.");

            var command = new AdminParsedCommand { commandType = commandType };

            if (string.IsNullOrEmpty(remainder))
                return AdminCommandParseResult.Ok(command);

            var rawTokens = remainder.Split(',').Select(t => t.Trim()).Where(t => t.Length > 0).ToList();

            string currentKey = null;
            bool firstToken = true;

            foreach (var token in rawTokens)
            {
                int colonIndex = token.IndexOf(':');

                if (colonIndex >= 0)
                {
                    string key = token.Substring(0, colonIndex).Trim().ToLowerInvariant();
                    string value = token.Substring(colonIndex + 1).Trim();

                    if (key.Length == 0)
                        return AdminCommandParseResult.Fail($"Malformed token '{token}' — empty key before ':'.");

                    currentKey = key;
                    firstToken = false;

                    if (!command.keyedArgs.ContainsKey(key))
                        command.keyedArgs[key] = new List<string>();

                    if (value.Length > 0)
                        command.keyedArgs[key].Add(value);
                }
                else
                {
                    if (firstToken)
                    {
                        command.positionalArg = token;
                        firstToken = false;
                        continue;
                    }

                    if (currentKey == null)
                        return AdminCommandParseResult.Fail(
                            $"Unexpected value '{token}' with no preceding key:value pair.");

                    command.keyedArgs[currentKey].Add(token);
                }
            }

            return AdminCommandParseResult.Ok(command);
        }
    }
}