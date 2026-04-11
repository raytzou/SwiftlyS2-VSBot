using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;

namespace VSBot.Commands
{
    public class Rcon : IRcon
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<Rcon> _logger;

        public Rcon(ISwiftlyCore core, ILogger<Rcon> logger)
        {
            _core = core;
            _logger = logger;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            if (context.Args.Length < 1)
            {
                context.Reply($"[sw] Usage: sw_rcon <rcon>");
                return;
            }

            var rcon = string.Join(" ", context.Args);
            var player = context.Sender;
            var admin = player?.Name ?? "Console";

            _core.Engine.ExecuteCommandWithBuffer(rcon, (output) =>
            {
                context.Reply(output);
                _logger.LogInformation("{admin} executed RCON command '{rcon}': {output}", admin, rcon, output);
            });
        }
    }
}
