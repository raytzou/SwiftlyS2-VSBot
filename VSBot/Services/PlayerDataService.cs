using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using VSBot.Domain.Entities;
using VSBot.Domain.Repositories.Interfaces;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class PlayerDataService : IPlayerDataService
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly ILogger<PlayerDataService> _logger;

        public PlayerDataService(IPlayerRepository playerRepository, ILogger<PlayerDataService> logger)
        {
            _playerRepository = playerRepository;
            _logger = logger;
        }

        private Dictionary<ulong, Player> _playerCaches = new();
        private HashSet<ulong> _modifiedCaches = new();

        public void PreparePlayerCache(IPlayer player)
        {
            if (_playerCaches.ContainsKey(player.SteamID))
                return;

            var playerData = _playerRepository.GetByKey(player.SteamID);
            var now = DateTime.Now;

            if (playerData is null)
            {
                playerData = new Player()
                {
                    SteamId = player.SteamID,
                    PlayerName = player.Name,
                    IpAddress = player.IPAddress,
                    LastTimeConnect = now,
                    DefaultSkinModelPath = string.Empty,
                    Volume = 50,
                    SaysoundVolume = 50,
                    Language = string.Empty
                };

                _playerRepository.Add(playerData);
            }
            else
            {
                playerData.PlayerName = player.Name;
                playerData.IpAddress = player.IPAddress;
                playerData.LastTimeConnect = now;

                _playerRepository.Update(playerData);
            }

            _playerCaches.Add(player.SteamID, playerData);
        }

        public Player? GetPlayerCache(ulong steamId) =>
            _playerCaches.GetValueOrDefault(steamId);

        public void UpdatePlayerCache(Player player)
        {
            _playerCaches[player.SteamId] = player;
            _modifiedCaches.Add(player.SteamId);
        }

        public void SavePlayerCacheToDB(Player player)
        {
            try
            {
                _playerRepository.Update(player);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save cache, {steamId}", player.SteamId);
            }

            _modifiedCaches.Remove(player.SteamId);
        }

        public void SaveAllPlayerCachesToDB()
        {
            if (_modifiedCaches.Count == 0)
                return;

            var players = _modifiedCaches
            .Select(id => _playerCaches[id])
            .ToList();

            try
            {
                _playerRepository.UpdateRange(players);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save {Count} player caches.", players.Count);
            }

            _modifiedCaches.Clear();
        }

        public void RemovePlayerCache(ulong steamId)
        {
            var player = _playerCaches[steamId];

            _modifiedCaches.Remove(steamId);
            _playerCaches.Remove(steamId);
        }

        public void SaveAndRemovePlayerCache(ulong steamId)
        {
            if (!_playerCaches.TryGetValue(steamId, out var player))
                return;

            if (_modifiedCaches.Contains(steamId))
                SavePlayerCacheToDB(player);

            RemovePlayerCache(steamId);
        }

        public void ClearPlayerCaches()
        {
            _playerCaches.Clear();
            _modifiedCaches.Clear();
        }
    }
}
