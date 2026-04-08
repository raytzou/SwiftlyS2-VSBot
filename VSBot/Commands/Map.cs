using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Classes;
using VSBot.Commands.Interfaces;

namespace VSBot.Commands
{
    public class Map : IMap
    {
        private readonly ILogger<Map> _logger;
        private readonly ISwiftlyCore _core;

        public Map(ILogger<Map> logger, ISwiftlyCore core)
        {
            _logger = logger;
            _core = core;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            if (context.Args.Length < 1)
            {
                context.Reply("[sw] Usage: sw_map <map name>");
                return;
            }

            var mapName = context.Args[0];

            if (!Utility.AllMaps.Any(m => m.Equals(mapName)))
            {
                context.Reply($"[sw] Map not found: {mapName}");
                return;
            }

            var player = context.Sender;

            _logger.LogInformation("{admin} changed map to {mapName} at {DT}", player?.Name is null ? "Console" : player.Name, mapName, DateTime.Now);
            _core.PlayerManager.SendChat($"Admin changed map to {mapName}");
            _core.Scheduler.DelayBySeconds(2, () =>
            {
                if (Utility.IsWorkshopMap(mapName))
                    _core.Engine.ExecuteCommand($"ds_workshop_changelevel {mapName}");
                else
                    _core.Engine.ExecuteCommand($"changelevel {mapName}");
            });
        }
    }
}
