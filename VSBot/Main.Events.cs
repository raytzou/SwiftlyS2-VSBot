using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Events;
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

            gameState.ClearPlayers();
            playerService.ClearPlayerPositions();
            playerService.ClearPlayerWeaponStatuses();
            Core.Logger.LogInformation("Map start: {mapName}", mapName);
        }
    }
}
