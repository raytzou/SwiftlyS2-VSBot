using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;
using VSBot.Services.Interfaces;

namespace VSBot.Commands
{
    public class Volume : IVolume
    {
        private readonly IPlayerDataService _playerDataService;
        private readonly ILogger<Volume> _logger;

        public Volume(IPlayerDataService playerDataService, ILogger<Volume> logger)
        {
            _playerDataService = playerDataService;
            _logger = logger;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            if (player is not { IsValid: true })
                return;

            var playerCache = _playerDataService.GetPlayerCache(player.SteamID);

            if (playerCache is null)
            {
                if (_playerDataService.GetPlayerCacheState(player.SteamID) == PlayerCacheState.Loading)
                {
                    context.Reply("[sw] Your data is still loading, please try again later.");
                    return;
                }

                context.Reply("Cannot set your volume, please contact admin!");
                _logger.LogError("Cannot find the player's cache, Steam ID: {steamId}", player.SteamID);
                return;
            }

            if (context.Args.Length > 1)
            {
                context.Reply("[sw] Usage: sw_volume <volume 0 - 100>");
                return;
            }

            if (context.Args.Length == 0)
            {
                context.Reply($"[sw] Your music volume: {playerCache.Volume}");
                return;
            }

            var arg = context.Args[0];

            if (!byte.TryParse(arg, out var volume))
            {
                context.Reply("[sw] Invalid volume");
                return;
            }

            if (volume < 0 || volume > 100)
            {
                context.Reply("[sw] Volume can only be set between 0 to 100");
                return;
            }

            playerCache.Volume = volume;
            _playerDataService.UpdatePlayerCache(playerCache);
            context.Reply($"[sw] Your volume has been set to {volume}");
        }
    }
}
