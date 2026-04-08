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
        private readonly IMap _map;
        private readonly ICvar _cvar;

        public CommandModule(ISwiftlyCore core, ILogger<CommandModule> logger
            , IInfo info
            , IMap map
            , ICvar cvar
            )
        {
            _core = core;
            _info = info;
            _logger = logger;
            _map = map;
            _cvar = cvar;
        }

        public void Initialize()
        {
            _core.Registrator.Register(this);
            _logger.LogInformation("Command Module loaded.");
        }

        [Command("info")]
        public void OnInfoCommand(ICommandContext context)
        {
            _info.ExecuteCommand(context);
        }

        [Command(name: "map", permission: "admin.changemap")]
        public void OnMapCommand(ICommandContext context)
        {
            _map.ExecuteCommand(context);
        }

        [Command(name: "cvar", permission: "admin.cvar")]
        public void OnCvarCommand(ICommandContext context)
        {
            _cvar.ExecuteCommand(context);
        }
    }
}
