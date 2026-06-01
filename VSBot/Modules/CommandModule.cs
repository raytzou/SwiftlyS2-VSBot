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
        private readonly IMaps _maps;
        private readonly ICvar _cvar;
        private readonly IRcon _rcon;
        private readonly IBuy _buy;
        private readonly IRevive _revive;
        private readonly ISlay _slay;

        public CommandModule(ISwiftlyCore core, ILogger<CommandModule> logger
            , IInfo info
            , IHelp help
            , IMap map
            , IMaps maps
            , ICvar cvar
            , IRcon rcon
            , IBuy buy
            , IRevive revive
            , ISlay slay
            )
        {
            _core = core;
            _logger = logger;
            _info = info;
            _help = help;
            _map = map;
            _maps = maps;
            _cvar = cvar;
            _rcon = rcon;
            _buy = buy;
            _revive = revive;
            _slay = slay;
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

        [Command(name: "maps", permission: "admin.changemap", helpText: "Lists all maps")]
        public void OnMapsCommand(ICommandContext context)
        {
            _maps.ExecuteCommand(context);
        }


        [Command(name: "cvar", permission: "admin.cvar", helpText: "Updates the specific Console Variable")]
        public void OnCvarCommand(ICommandContext context)
        {
            _cvar.ExecuteCommand(context);
        }

        [Command(name: "rcon", permission: "root", helpText: "Uses Remote Console")]
        public void OnRconCommand(ICommandContext context)
        {
            _rcon.ExecuteCommand(context);
        }

        [Command(name: "buy", helpText: "Buy items")]
        public void OnBuyCommand(ICommandContext context)
        {
            _buy.ExecuteCommand(context);
        }

        [Command(name: "revive")]
        [CommandAlias("res")]
        public void OnReviveCommand(ICommandContext context)
        {
            _revive.ExecuteCommand(context);
        }

        [Command(name: "slay", permission: "admin.slay", helpText: "Slays the player(s)")]
        public void OnSlayCommand(ICommandContext context)
        {
            _slay.ExecuteCommand(context);
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
