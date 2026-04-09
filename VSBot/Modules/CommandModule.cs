using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;

namespace VSBot.Modules
{
    public class CommandModule
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<CommandModule> _logger;
        private readonly IInfo _info;
        private readonly IHelp _help;
        private readonly IMap _map;
        private readonly ICvar _cvar;

        public CommandModule(ISwiftlyCore core, ILogger<CommandModule> logger
            , IInfo info
            , IHelp help
            , IMap map
            , ICvar cvar
            )
        {
            _core = core;
            _logger = logger;
            _info = info;
            _help = help;
            _map = map;
            _cvar = cvar;
        }

        public void Initialize()
        {
            _core.Registrator.Register(this);
            _logger.LogInformation("Command Module loaded.");
        }

        [Command(name: "info", helpText: "Prints server information")]
        public void OnInfoCommand(ICommandContext context)
        {
            _info.ExecuteCommand(context);
        }

        [Command(name: "help", helpText: "Lists all available commands")]
        public void OnHelpCommand(ICommandContext context)
        {
            _help.ExecuteCommand(context);
        }

        [Command(name: "map", permission: "admin.changemap", helpText: "Changes the current map to the specific map")]
        public void OnMapCommand(ICommandContext context)
        {
            _map.ExecuteCommand(context);
        }

        [Command(name: "cvar", permission: "admin.cvar", helpText: "Update the specific Console Variable")]
        public void OnCvarCommand(ICommandContext context)
        {
            _cvar.ExecuteCommand(context);
        }

        /// <summary>
        /// Debug-only. Implement temporarily for local testing only.
        /// Do not commit.
        /// </summary>
        /// <param name="context"></param>
        [Command(name: "debug", permission: "root", helpText: "*")]
        public void OnDebugCommand(ICommandContext context)
        {

        }
    }
}
