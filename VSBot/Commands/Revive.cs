using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using VSBot.Commands.Interfaces;
using VSBot.Models;
using VSBot.Services.Interfaces;

namespace VSBot.Commands
{
    public class Revive : IRevive
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<Revive> _logger;
        private readonly IOptionsMonitor<Config> _config;
        private readonly IPlayerService _playerService;
        private readonly IGameStateService _gameStateService;

        private Dictionary<ulong, CancellationTokenSource?> _reviveTimers = [];

        public Revive(ISwiftlyCore core, ILogger<Revive> logger, IOptionsMonitor<Config> config, IPlayerService playerService, IGameStateService gameStateService)
        {
            _core = core;
            _logger = logger;
            _config = config;
            _playerService = playerService;
            _gameStateService = gameStateService;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;
            if (!context.IsSentByPlayer || player is null || !player.IsValid) return;


            if (player.PlayerPawn is null || !player.PlayerPawn.IsValid)
            {
                context.Reply("Cannot revive!");
                _logger.LogWarning("Player {playerName} PlayerPawn is invalid when revive!", player?.Name);
                return;
            }

            var reviveCost = _config.CurrentValue.ReviveCostScore;

            if (player.PlayerPawn.Team == SwiftlyS2.Shared.Players.Team.Spectator)
            {
                context.Reply("[sw] You are in spectator mode, cannot revive.");
                return;
            }

            if (player.IsAlive)
            {
                context.Reply("[sw] You are alive.");
                return;
            }

            if (player.Controller.Score - reviveCost < 0 && !_config.CurrentValue.IsDebug)
            {
                context.Reply($"[sw] You don't have enough score ({reviveCost}) to revive.");
                return;
            }

            var position = _playerService.GetPlayerPosition(player.SteamID);
            if (position is null)
            {
                context.Reply("Cannot revive!");
                _logger.LogWarning("Player {playerName} Position is null!", player.Name);
                return;
            }

            if (position.Origin == default || position.Rotation == default || position.Velocity == default)
            {
                var humanTeam = _gameStateService.HumanTeam;
                SpawnPoint humanSpawnPoint;

                if (humanTeam == Team.T)
                {
                    humanSpawnPoint = _core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_terrorist").First();
                }
                else
                {
                    humanSpawnPoint = _core.EntitySystem.GetAllEntitiesByDesignerName<SpawnPoint>("info_player_counterterrorist").First();
                }

                position.Origin = humanSpawnPoint.AbsOrigin ?? new();
                position.Rotation = humanSpawnPoint.AbsRotation ?? new();
                position.Velocity = humanSpawnPoint.AbsVelocity;
            }

            if (_config.CurrentValue.IsDebug)
                context.Reply($"X: {position.Origin.X} Y: {position.Origin.Y} Z: {position.Origin.Z}");
            else
            {
                player.Controller.Score -= 50;
                player.Controller.ScoreUpdated();
            }

            int reviveCountDown = 5;

            CancelReviveTimer(player.SteamID);
            _reviveTimers[player.SteamID] = _core.Scheduler.RepeatBySeconds(1f, () =>
            {
                ReviveCallBack(ref reviveCountDown, player, position);
            });
        }

        private void ReviveCallBack(ref int time, IPlayer player, PlayerPosition position)
        {
            if (time > 0)
            {
                player.SendCenter($"Revive in {time--}");
                return;
            }

            player.SendCenter($"You have been revived.");
            player.Respawn();

            if (player.PlayerPawn is null || !player.PlayerPawn.IsValid)
            {
                _logger.LogWarning("{playerName} PlayerPawn is null or invalid after reviving", player.Name);
                CancelReviveTimer(player.SteamID);
                return;
            }

            player.PlayerPawn.Teleport(position.Origin, position.Rotation, position.Velocity);

            var weaponService = player.PlayerPawn.WeaponServices;

            if (weaponService is null || !weaponService.IsValid)
            {
                _logger.LogWarning("{playerName} WeaponService is null or invalid after reviving", player.Name);
                CancelReviveTimer(player.SteamID);
                return;
            }

            if (player.PlayerPawn.ItemServices is null || !player.PlayerPawn.ItemServices.IsValid)
            {
                _logger.LogWarning("{playerName} ItemServices is null or invalid after reviving", player.Name);
                CancelReviveTimer(player.SteamID);
                return;
            }

            var weaponStatus = _playerService.GetPlayerWeaponStatus(player.SteamID);

            if (weaponStatus.Weapons.Count != 0)
            {
                foreach (var weapon in weaponService.MyWeapons)
                {
                    if (!weapon.IsValid || weapon.Value is null || !weapon.Value.IsValid)
                        continue;

                    weaponService.RemoveWeapon(weapon.Value);
                }

                foreach (var weapon in weaponStatus.Weapons)
                {
                    player.PlayerPawn.ItemServices.GiveItem(weapon);
                }
            }

            CancelReviveTimer(player.SteamID);
        }

        private void CancelReviveTimer(ulong steamID)
        {
            if (_reviveTimers.TryGetValue(steamID, out var timer))
            {
                timer?.Cancel();
                timer?.Dispose();
                _reviveTimers.Remove(steamID);
            }
        }

        public void OnPlayerDisconnect(ulong steamID)
        {
            CancelReviveTimer(steamID);
        }

        public void OnRoundEnd()
        {
            foreach (var steamID in _reviveTimers.Keys.ToList())
            {
                CancelReviveTimer(steamID);
            }
        }
    }
}
