using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared.Events;
using VSBot.Models;
using VSBot.Modules;
using VSBot.Services.Interfaces;

namespace VSBot
{
    public partial class Main
    {
        private void OnMapLoad(IOnMapLoadEvent @event)
        {
            var mapName = @event.MapName;
            var gameState = GetRequiredService<IGameStateService>();
            var playerService = GetRequiredService<IPlayerService>();
            var config = GetRequiredService<IOptionsMonitor<Config>>();
            var botModule = GetRequiredService<BotModule>();

            if (config.CurrentValue.IsDebug)
            {
                Core.Logger.LogWarning("Server debug mode is on");
                gameState.MapReady += EnableCheats;
            }

            gameState.ClearPlayers();
            playerService.ClearPlayerPositions();
            playerService.ClearPlayerWeaponStatuses();
            gameState.OnMapStart();
            botModule.OnMapStart();
            Core.Logger.LogInformation("Map start: {mapName}", mapName);
        }

        private void EnableCheats()
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.MapReady -= EnableCheats;
            Core.Engine.ExecuteCommand("sv_cheats 1");
        }
    }
}
