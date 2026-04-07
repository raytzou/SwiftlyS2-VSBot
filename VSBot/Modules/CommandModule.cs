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

        public CommandModule(ISwiftlyCore core, ILogger<CommandModule> logger
            , IInfo info
            )
        {
            _core = core;
            _info = info;
            _logger = logger;
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
    }
}
