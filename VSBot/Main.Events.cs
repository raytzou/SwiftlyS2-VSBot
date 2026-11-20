using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Schemas;
using VSBot.Models;
using VSBot.Modules;
using VSBot.Services.Interfaces;

namespace VSBot
{
    public partial class Main
    {
        private int _mapGeneration;
        private bool _mapStarted;
        private bool _mapLoading;

        private void OnMapLoad(IOnMapLoadEvent @event)
        {
            CancelMapInitialization();
            _mapLoading = true;
            if (GetRequiredService<IOptionsMonitor<Config>>().CurrentValue.IsDebug)
                Core.Logger.LogInformation("Map loading: {Map}, generation {Generation}.", @event.MapName, _mapGeneration);
        }

        private void OnStartupServer()
        {
            if (!_mapLoading || _mapStarted) return;
            var generation = _mapGeneration;
            // OnMapLoad can run in the old world. Wait for the new server's first world update.
            Core.Scheduler.NextWorldUpdate(() =>
            {
                if (generation != _mapGeneration || !_mapLoading || _mapStarted) return;
                _mapStarted = true;
                InitializeMap();
            });
        }

        private void InitializeMap()
        {
            var mapName = Core.Engine.GlobalVars.MapName;
            var gameState = GetRequiredService<IGameStateService>();
            var playerService = GetRequiredService<IPlayerService>();
            var config = GetRequiredService<IOptionsMonitor<Config>>();
            var botModule = GetRequiredService<BotModule>();
            var playerDataService = GetRequiredService<IPlayerDataService>();

            if (config.CurrentValue.IsDebug)
            {
                Core.Logger.LogWarning("Server debug mode is on");
                gameState.MapReady += EnableCheats;
            }

            ResetDefaultWeapon();
            ResetMaxRounds();
            gameState.MapReady += AddMoreSpawnPoints;
            gameState.MapReady += SetHumanTeam;
            gameState.ClearPlayers();
            playerService.ClearPlayerPositions();
            playerService.ClearPlayerWeaponStatuses();
            gameState.OnMapStart();
            botModule.OnMapStart();
            playerDataService.ClearPlayerCaches();
            Core.Logger.LogInformation("Map start: {mapName}", mapName);
        }

        private void OnMapUnload(IOnMapUnloadEvent @event)
        {
            CancelMapInitialization();
            GetRequiredService<UIModule>().OnMapUnload();

            var playerDataService = GetRequiredService<IPlayerDataService>();
            var musicService = GetRequiredService<IMusicService>();

            playerDataService.SaveAllPlayerCachesToDB();
            playerDataService.ClearPlayerCaches();
            musicService.Reset();
        }

        private void CancelMapInitialization()
        {
            ++_mapGeneration;
            _mapStarted = false;
            _mapLoading = false;
            var gameState = GetRequiredService<IGameStateService>();
            gameState.OnMapUnload();
            gameState.MapReady -= EnableCheats;
            gameState.MapReady -= AddMoreSpawnPoints;
            gameState.MapReady -= SetHumanTeam;
            GetRequiredService<BotModule>().ClearMapResources();
        }

        private void OnPrecacheResource(IOnPrecacheResourceEvent @event)
        {
            var config = GetRequiredService<IOptionsMonitor<Config>>();
            var saysoundSoundEventFile = config.CurrentValue.SaysoundSoundEventFile;
            var soundEventFile = config.CurrentValue.SoundEventFile;

            if (string.IsNullOrEmpty(saysoundSoundEventFile))
            {
                throw new InvalidOperationException("Saysound Sound Event File is not set.");
            }

            if (string.IsNullOrEmpty(soundEventFile))
            {
                throw new InvalidOperationException("Sound Event File is not set.");
            }

            @event.AddItem(saysoundSoundEventFile);
            @event.AddItem(soundEventFile);
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

            DuplicateSpawnPoints<CInfoPlayerTerrorist>(TSpawnPoints, "T", "info_player_terrorist", spawnPointCount);
            DuplicateSpawnPoints<CInfoPlayerCounterterrorist>(CTSpawnPoints, "CT", "info_player_counterterrorist", spawnPointCount);
            var generation = _mapGeneration;
            Core.Scheduler.NextWorldUpdate(() =>
            {
                if (generation != _mapGeneration || !_mapStarted) return;
                var TSpawnPointCount = Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").Count();
                var CTSpawnPointCount = Core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").Count();

                Core.Logger.LogInformation("TSpawnPointCount: {count}", TSpawnPointCount);
                Core.Logger.LogInformation("CTSpawnPoints: {count}", CTSpawnPointCount);
            });
        }

        private void DuplicateSpawnPoints<T>(List<SpawnPoint> spawnPoints, string teamLabel, string designerName, int targetCount)
            where T : class, SpawnPoint, ISchemaClass<T>
        {
            if (spawnPoints.Count == 0 || spawnPoints.Count >= targetCount) return;

            int needed = targetCount - spawnPoints.Count;
            Core.Logger.LogInformation("{team} team has {current} spawn points, duplicating {needed} more to reach {target}",
                teamLabel, spawnPoints.Count, needed, targetCount);

            for (int i = 0; i < needed; i++)
            {
                var src = spawnPoints[Random.Shared.Next(spawnPoints.Count)];
                var newPoint = Core.EntitySystem.CreateEntityByDesignerName<T>(designerName);
                newPoint!.Teleport(src.AbsOrigin, src.AbsRotation, src.AbsVelocity);
                newPoint.DispatchSpawn();
            }
        }

        private void ResetDefaultWeapon()
        {
            Core.Engine.ExecuteCommand("mp_ct_default_melee weapon_knife");
            Core.Engine.ExecuteCommand("mp_t_default_melee weapon_knife");
            Core.Engine.ExecuteCommand("mp_ct_default_primary \"\"");
            Core.Engine.ExecuteCommand("mp_t_default_primary \"\"");
            Core.Engine.ExecuteCommand("mp_ct_default_secondary weapon_hkp2000");
            Core.Engine.ExecuteCommand("mp_t_default_secondary weapon_glock");
        }

        private void ResetMaxRounds()
        {
            var config = GetRequiredService<IOptionsMonitor<Config>>();
            var maxRounds = config.CurrentValue.MaxRounds;

            Core.Engine.ExecuteCommand($"mp_maxrounds {maxRounds}");
        }

        private void SetHumanTeam()
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.MapReady -= SetHumanTeam;

            if (gameState.HumanTeam == SwiftlyS2.Shared.Players.Team.CT)
            {
                Core.Engine.ExecuteCommand("mp_humanteam ct");
            }
            else
            {
                Core.Engine.ExecuteCommand("mp_humanteam t");
            }
        }

    }
}
