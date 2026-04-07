using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using VSBot.Classes;
using VSBot.Commands.Interfaces;

namespace VSBot.Commands
{
    public class Map : IMap
    {
        private readonly ILogger<Map> _logger;

        public Map(ILogger<Map> logger)
        {
            _logger = logger;
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
            Main.SwiftlyCore.PlayerManager.SendChat($"Admin changed map to {mapName}");
            Main.SwiftlyCore.Scheduler.DelayBySeconds(2, () =>
            {
                if (Utility.IsWorkshopMap(mapName))
                    Main.SwiftlyCore.Engine.ExecuteCommand($"ds_workshop_changelevel {mapName}");
                else
                    Main.SwiftlyCore.Engine.ExecuteCommand($"changelevel {mapName}");
            });
        }
    }
}
