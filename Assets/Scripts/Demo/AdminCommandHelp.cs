using System.Collections.Generic;

namespace DDD.TNFY.BRAWL
{
    public static class AdminCommandHelp
    {
        private const string Divider =
            "---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------";

        private static readonly Dictionary<AdminCommandType, string> CommandHelp =
            new Dictionary<AdminCommandType, string>
            {
                {
                    AdminCommandType.Spawn,
                    "> /spawn CharacterName, abilities:A, B, C, passive:P, current_health:X, max_health:X, speed:X, attack:X, defense:X, jump_range:X, team:X, ai:true/false\n" +
                    "    Spawns CharacterName on the tile you click next. abilities/passive must exist on that character's CharacterData.\n" +
                    "    If abilities: is omitted, the character's default abilities are used (set on the CharacterData asset). Giving any abilities: overrides the default entirely (no top-up to 3). There is no default passive; omitting passive: means none.\n" +
                    "    team:X sets allegiance (0, 1, 2, ...). ai:true/false forces AI or player control regardless of prefab setup.\n" +
                    "    e.g. /spawn Lorns, abilities:Straight_Shot, Teleport, Recoil_Shot, passive:Mastermind, team:0, ai:false"
                },
                {
                    AdminCommandType.Kill,
                    "> /kill\n" +
                    "    Click a unit to instantly kill it."
                },
                {
                    AdminCommandType.Delete,
                    "> /delete\n" +
                    "    Click a unit to instantly and completely remove it - no death animation, no body left behind,\n" +
                    "    removed from the turn order immediately. Works on living units and existing bodies alike."
                },
                {
                    AdminCommandType.Heal,
                    "> /heal amount:X\n" +
                    "    Click a unit to heal it. Does not overheal past max health. amount must be >= 0."
                },
                {
                    AdminCommandType.Damage,
                    "> /damage amount\n" +
                    "    Click a unit to deal amount damage through the normal pipeline - respects Shielded,\n" +
                    "    Immune, Guarded, and Alerted (dodge). amount must be >= 0."
                },
                {
                    AdminCommandType.TrueDamage,
                    "> /truedamage amount\n" +
                    "    Click a unit to deal amount damage bypassing all statuses.\n" +
                    "    Passive-driven damage modifiers still apply. amount must be >= 0."
                },
                {
                    AdminCommandType.StatusEffect,
                    "> /statuseffect effect_name, duration:2, power:1\n" +
                    "    Click a unit to apply effect_name. duration is always valid (default 2). power is optional and any whole number (including negative or 0); it sets the effect's magnitude (e.g. stacks for AddStacks effects like Bleeding, or +10% per point for AttackUp/DefenseUp/etc). Effects that don't read power ignore it harmlessly."
                },
                {
                    AdminCommandType.Teleport,
                    "> /teleport\n" +
                    "    Click a tile to teleport the current active unit there."
                },
                {
                    AdminCommandType.Skip,
                    "> /skip\n" +
                    "    Click a unit to force-end the current turn and jump straight to that unit's turn.\n" +
                    "    Units in between get no turn at all."
                },
                {
                    AdminCommandType.Reset,
                    "> /reset\n" +
                    "    Instantly restarts the entire combat from the beginning - reloads the scene, undoing every\n" +
                    "    admin command, kill, and action taken this session. No confirmation prompt, no target."
                },
                {
                    AdminCommandType.Help,
                    "> /help\n" +
                    "    Shows this message.\n" +
                    "> /help command_name\n" +
                    "    Shows help for just that command."
                },
            };

        public static string GetHelpText()
        {
            string body = string.Join("\n\n", CommandHelp.Values);

            return
                Divider + "\n" +
                "Admin console commands - type the command, press Enter, then click the target if prompted.\n" +
                "\n" +
                body + "\n" +
                "\n" +
                "Note: Commands only work on your turn, when nothing is mid-animation.\n" +
                Divider;
        }

        public static string GetHelpText(string commandName)
        {
            if (string.IsNullOrWhiteSpace(commandName))
                return GetHelpText();

            var parseResult = AdminCommandParser.Parse("/" + commandName.Trim());
            if (!parseResult.success || !CommandHelp.TryGetValue(parseResult.command.commandType, out string entry))
                return $"No help found for '{commandName}'. Type /help to see all commands.";

            return Divider + "\n" + entry + "\n" + Divider;
        }
    }
}