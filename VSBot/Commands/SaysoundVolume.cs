using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;
using VSBot.Services.Interfaces;

namespace VSBot.Commands
{
    public class SaysoundVolume : ISaysoundVolume
    {
        private readonly ILogger<SaysoundVolume> _logger;
        private readonly IPlayerDataService _playerDataService;

        public SaysoundVolume(ILogger<SaysoundVolume> logger, IPlayerDataService playerDataService)
        {
            _logger = logger;
            _playerDataService = playerDataService;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            if (player is null || !player.IsValid || player.PlayerPawn is null || !player.PlayerPawn.IsValid)
                return;

            if (context.Args.Length > 1)
            {
                context.Reply("[sw] Usage: sw_ss_vol <Saysound Volume 0 - 100>");
                return;
            }

            var playerCache = _playerDataService.GetPlayerCache(player.SteamID);

            if (playerCache is null)
            {
                context.Reply("Cannot set the volume of saysound, please contact admin!");
                _logger.LogError("Cannot find the player's cache, {steamId}", player.SteamID);
                return;
            }

            if (context.Args.Length == 0)
            {
                context.Reply($"Saysound volume: {playerCache.SaysoundVolume}");
                return;
            }

            var arg = context.Args[0];

            if (!byte.TryParse(arg, out var volume))
            {
                context.Reply("[sw] Volume is invaild");
                return;
            }

            if (volume < 0 || volume > 100)
            {
                context.Reply("[sw] Volume is only allowed from 0 - 100");
                return;
            }

            playerCache.SaysoundVolume = volume;
            _playerDataService.UpdatePlayerCache(playerCache);
            context.Reply($"[sw] Your saysound volume has been set to {volume}");
        }
    }
}
