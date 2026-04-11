using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot.Commands
{
    public class Help : IHelp
    {
        private readonly ISwiftlyCore _core;

        public Help(ISwiftlyCore core)
        {
            _core = core;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;
            var isConsole = !context.IsSentByPlayer;
            var commands = _core.Command.GetAllCommandsInfo()
                .ExceptBy(["debug"], e => e.CommandName)
                .Where(w =>
                    isConsole ||
                    string.IsNullOrEmpty(w.Permission) ||
                    _core.Permission.PlayerHasPermission(player!.SteamID, w.Permission))
                .OrderBy(o => o.CommandName)
                .ToList();

            if (commands.Count == 0)
            {
                context.Reply("[sw] No commands available");
                return;
            }

            var adminCmds = new List<CommandInfo>();
            var commonCmds = new List<CommandInfo>();

            foreach (var cmd in commands)
            {
                var needPermission = !string.IsNullOrEmpty(cmd.Permission);

                if (needPermission)
                {
                    adminCmds.Add(cmd);
                }
                else
                {
                    commonCmds.Add(cmd);
                }
            }

            context.Reply($" {ChatColors.Default}===== {ChatColors.Purple}Available Commands {ChatColors.Default}=====");

            if (adminCmds.Count != 0)
            {
                context.Reply($" {ChatColors.Red}Admin Commands:");

                foreach (var cmd in adminCmds)
                {
                    context.Reply($" {ChatColors.Yellow}{cmd.CommandName} {ChatColors.Grey}[{ChatColors.Orange}{cmd.Permission}{ChatColors.Grey}] - {cmd.HelpText}");
                }
            }

            if (commonCmds.Count != 0)
            {
                context.Reply($" {ChatColors.Green}Player Commands:");

                foreach (var cmd in commonCmds)
                {
                    context.Reply($" {ChatColors.Yellow}{cmd.CommandName} {ChatColors.Grey}- {cmd.HelpText}");
                }
            }

            context.Reply($" {ChatColors.Grey}==================");
            context.Reply($"Total: {ChatColors.Lime}{commands.Count} {ChatColors.Grey}commands");
        }
    }
}
