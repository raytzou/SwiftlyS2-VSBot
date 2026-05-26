using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SchemaDefinitions;
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

            gameState.MapReady += AddMoreSpawnPoints;
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

        private void AddMoreSpawnPoints()
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.MapReady -= AddMoreSpawnPoints;

            var TSpawnPoints = Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").ToList();
            var CTSpawnPoints = Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").ToList();
            var mapName = Core.Engine.GlobalVars.MapName;
            var config = GetRequiredService<IOptionsMonitor<Config>>();
            var spawnPointCount = config.CurrentValue.SpawnPointCount;

            if (TSpawnPoints.Count == 0)
                Core.Logger.LogWarning("T Spawn point not found. map: {mapName}", mapName);

            if (CTSpawnPoints.Count == 0)
                Core.Logger.LogWarning("CT Spawn point not found. map: {mapName}", mapName);

            // Check and duplicate T spawn points if needed
            if (TSpawnPoints.Count > 0 && TSpawnPoints.Count < spawnPointCount)
            {
                int neededSpawns = spawnPointCount - TSpawnPoints.Count;
                Core.Logger.LogInformation("T team has {current} spawn points, duplicating {needed} more to reach {target}",
                    TSpawnPoints.Count, neededSpawns, spawnPointCount);

                for (int i = 0; i < neededSpawns; i++)
                {
                    var randomSpawnPoint = TSpawnPoints[Random.Shared.Next(TSpawnPoints.Count)];
                    var newTSpawnPoint = Core.EntitySystem.CreateEntityByDesignerName<CInfoPlayerTerrorist>("info_player_terrorist");
                    newTSpawnPoint!.Teleport(randomSpawnPoint.AbsOrigin, randomSpawnPoint.AbsRotation, randomSpawnPoint.AbsVelocity);
                    newTSpawnPoint.DispatchSpawn();
                }
            }

            // Check and duplicate CT spawn points if needed
            if (CTSpawnPoints.Count > 0 && CTSpawnPoints.Count < spawnPointCount)
            {
                int neededSpawns = spawnPointCount - CTSpawnPoints.Count;
                Core.Logger.LogInformation("CT team has {current} spawn points, duplicating {needed} more to reach {target}",
                    CTSpawnPoints.Count, neededSpawns, spawnPointCount);

                for (int i = 0; i < neededSpawns; i++)
                {
                    var randomSpawnPoint = CTSpawnPoints[Random.Shared.Next(CTSpawnPoints.Count)];
                    var newCTSpawnPoint = Core.EntitySystem.CreateEntityByDesignerName<CInfoPlayerCounterterrorist>("info_player_counterterrorist");
                    newCTSpawnPoint!.Teleport(randomSpawnPoint.AbsOrigin, randomSpawnPoint.AbsRotation, randomSpawnPoint.AbsVelocity);
                    newCTSpawnPoint.DispatchSpawn();
                }
            }
            Core.Scheduler.NextWorldUpdate(() =>
            {
                var TSpawnPointCount = Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").Count();
                var CTSpawnPointCount = Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").Count();

                Core.Logger.LogInformation("TSpawnPointCount: {count}", TSpawnPointCount);
                Core.Logger.LogInformation("CTSpawnPoints: {count}", CTSpawnPointCount);
            });
        }
    }
}
