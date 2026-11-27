using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using VSBot.Domain.Entities;
using VSBot.Domain.Repositories.Interfaces;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class PlayerDataService : IPlayerDataService
    {
        private readonly IPlayerRepository _playerRepository;
        private readonly IDatabaseWorker _databaseWorker;
        private readonly ISwiftlyCore _core;
        private readonly ILogger<PlayerDataService> _logger;

        public PlayerDataService(IPlayerRepository playerRepository, IDatabaseWorker databaseWorker, ISwiftlyCore core, ILogger<PlayerDataService> logger)
        {
            _playerRepository = playerRepository;
            _databaseWorker = databaseWorker;
            _core = core;
            _logger = logger;
        }

        // Game thread only. The entry instance is the session identity (ReferenceEquals).
        private sealed class PlayerCacheEntry
        {
            public PlayerCacheState State = PlayerCacheState.Loading;
            public Player? Data;
            private readonly List<(Action<Player> Callback, string Caller)> _pendingCallbacks = [];

            public void AddPendingCallback(Action<Player> callback, string caller) =>
                _pendingCallbacks.Add((callback, caller));

            public (Action<Player> Callback, string Caller)[] TakePendingCallbacks()
            {
                var callbacks = _pendingCallbacks.ToArray();
                _pendingCallbacks.Clear();
                return callbacks;
            }

            public void ClearPendingCallbacks() => _pendingCallbacks.Clear();
        }

        private Dictionary<ulong, PlayerCacheEntry> _playerCaches = new();
        private HashSet<ulong> _modifiedCaches = new();
        private volatile bool _isShutdown;

        private static readonly TimeSpan WarmUpTimeout = TimeSpan.FromSeconds(10);

        public void WarmUp()
        {
            var accepted = _databaseWorker.TryEnqueue(new DbWorkItem("WarmUp", null, WarmUpAsync));

            if (!accepted)
                _logger.LogWarning("Database queue rejected the warm-up request.");
        }

        private async Task WarmUpAsync(CancellationToken cancellationToken)
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(WarmUpTimeout);

            try
            {
                await _playerRepository.GetByKeyAsync(0, timeoutSource.Token);
                _logger.LogInformation("Database warm-up completed.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Database warm-up timed out after {timeout}.", WarmUpTimeout);
            }
        }

        public void PrepareShutdown()
        {
            SaveAllPlayerCachesToDB();
            ClearPlayerCaches();
            _isShutdown = true;
        }

        public void PreparePlayerCache(IPlayer player)
        {
            if (_isShutdown) return;

            var steamId = player.SteamID;

            if (_playerCaches.ContainsKey(steamId))
                return;

            var name = player.Name;
            var ipAddress = player.IPAddress;
            var now = DateTime.Now;
            var entry = new PlayerCacheEntry();

            _playerCaches[steamId] = entry;

            var accepted = _databaseWorker.TryEnqueue(new DbWorkItem(
                "LoadPlayer",
                steamId,
                cancellationToken => LoadPlayerAsync(entry, steamId, name, ipAddress, now, cancellationToken)));

            if (!accepted)
            {
                entry.State = PlayerCacheState.LoadFailed;
                entry.ClearPendingCallbacks();
                _logger.LogWarning("Database queue rejected the load request, SteamID: {steamId}", steamId);
            }
        }

        // Runs on the database worker. Must not touch the cache or the entry.
        private async Task LoadPlayerAsync(PlayerCacheEntry entry, ulong steamId, string name, string ipAddress, DateTime now, CancellationToken cancellationToken)
        {
            Player? result = null;

            try
            {
                result = await LoadOrCreateAsync(steamId, name, ipAddress, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load player data, SteamID: {steamId}", steamId);
            }

            if (_isShutdown)
                return;

            _core.Scheduler.NextWorldUpdate(() => ApplyLoadResult(entry, steamId, result));
        }

        private async Task<Player> LoadOrCreateAsync(ulong steamId, string name, string ipAddress, DateTime now, CancellationToken cancellationToken)
        {
            var existing = await _playerRepository.GetByKeyAsync(steamId, cancellationToken);

            if (existing is null)
            {
                var created = new Player()
                {
                    SteamId = steamId,
                    PlayerName = name,
                    IpAddress = ipAddress,
                    LastTimeConnect = now,
                    DefaultSkinModelPath = string.Empty,
                    Volume = 50,
                    SaysoundVolume = 50,
                    Language = string.Empty
                };

                await _playerRepository.AddAsync(created, cancellationToken);

                return created;
            }

            existing.PlayerName = name;
            existing.IpAddress = ipAddress;
            existing.LastTimeConnect = now;

            try
            {
                await _playerRepository.UpdateAsync(existing, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to update player connect info, SteamID: {steamId}", steamId);
            }

            return existing;
        }

        // Runs on the game thread.
        private void ApplyLoadResult(PlayerCacheEntry entry, ulong steamId, Player? result)
        {
            try
            {
                if (_isShutdown)
                {
                    entry.ClearPendingCallbacks();
                    return;
                }

                if (!_playerCaches.TryGetValue(steamId, out var current)
                    || !ReferenceEquals(current, entry)
                    || entry.State != PlayerCacheState.Loading)
                {
                    entry.ClearPendingCallbacks();
                    return;
                }

                if (result is null)
                {
                    entry.State = PlayerCacheState.LoadFailed;
                    foreach (var pending in entry.TakePendingCallbacks())
                        _logger.LogWarning("Cache load failed; callback discarded. SteamID: {SteamId}, Caller: {Caller}", steamId, pending.Caller);
                    return;
                }

                entry.Data = result;
                entry.State = PlayerCacheState.Ready;
                _logger.LogInformation("Player cache added: {steamId}", steamId);

                foreach (var pending in entry.TakePendingCallbacks())
                {
                    if (_isShutdown || !_playerCaches.TryGetValue(steamId, out current)
                        || !ReferenceEquals(current, entry) || entry.State != PlayerCacheState.Ready
                        || !ReferenceEquals(entry.Data, result))
                        break;

                    InvokeCacheReadyCallback(steamId, result, pending.Callback, pending.Caller);
                }
            }
            catch (Exception ex)
            {
                if (entry.State == PlayerCacheState.Loading)
                    entry.State = PlayerCacheState.LoadFailed;
                entry.ClearPendingCallbacks();
                _logger.LogError(ex, "Failed to apply player load result, SteamID: {steamId}", steamId);
            }
        }

        /// <inheritdoc />
        public void WhenPlayerCacheReady(ulong steamId, Action<Player> callback, [CallerMemberName] string caller = "")
        {
            ArgumentNullException.ThrowIfNull(callback);

            if (_isShutdown || !_playerCaches.TryGetValue(steamId, out var entry))
            {
                _logger.LogWarning("Cannot register cache callback: {Reason}. SteamID: {SteamId}, Caller: {Caller}",
                    _isShutdown ? "service shut down" : "cache not prepared", steamId, caller);
                return;
            }

            switch (entry.State)
            {
                case PlayerCacheState.Loading:
                    entry.AddPendingCallback(callback, caller);
                    return;
                case PlayerCacheState.Ready:
                    if (entry.Data is not { } data)
                    {
                        _logger.LogError("Ready cache has no data. SteamID: {SteamId}, Caller: {Caller}", steamId, caller);
                        return;
                    }
                    InvokeCacheReadyCallback(steamId, data, callback, caller);
                    return;
                case PlayerCacheState.LoadFailed:
                    _logger.LogWarning("Cannot register callback: cache load failed. SteamID: {SteamId}, Caller: {Caller}", steamId, caller);
                    return;
            }
        }

        private void InvokeCacheReadyCallback(ulong steamId, Player data, Action<Player> callback, string caller)
        {
            try
            {
                callback(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache-ready callback failed. SteamID: {SteamId}, Caller: {Caller}", steamId, caller);
            }
        }

        public Player? GetPlayerCache(ulong steamId) =>
            _playerCaches.TryGetValue(steamId, out var entry) && entry.State == PlayerCacheState.Ready
                ? entry.Data
                : null;

        public PlayerCacheState? GetPlayerCacheState(ulong steamId) =>
            _playerCaches.TryGetValue(steamId, out var entry) ? entry.State : null;

        public void UpdatePlayerCache(Player player)
        {
            if (!_playerCaches.TryGetValue(player.SteamId, out var entry) || entry.State != PlayerCacheState.Ready || !ReferenceEquals(entry.Data, player))
            {
                _logger.LogWarning("Ignore update, player cache is not ready: {steamId}", player.SteamId);
                return;
            }

            entry.Data = player;
            _modifiedCaches.Add(player.SteamId);
        }

        public void SavePlayerCacheToDB(Player player)
        {
            if (!_playerCaches.TryGetValue(player.SteamId, out var entry) || entry.State != PlayerCacheState.Ready || entry.Data is null)
                return;

            var snapshot = CreateSnapshot(entry.Data);
            var accepted = _databaseWorker.TryEnqueue(new DbWorkItem(
                "SavePlayer",
                snapshot.SteamId,
                cancellationToken => _playerRepository.UpdateAsync(snapshot, cancellationToken)));

            if (!accepted)
            {
                _logger.LogWarning("Database queue rejected the save request, SteamID: {steamId}", snapshot.SteamId);
                return;
            }

            _modifiedCaches.Remove(snapshot.SteamId);
        }

        public void SaveAllPlayerCachesToDB()
        {
            if (_modifiedCaches.Count == 0)
                return;

            var savedIds = new List<ulong>();
            var snapshots = new List<Player>();

            foreach (var steamId in _modifiedCaches)
            {
                if (_playerCaches.TryGetValue(steamId, out var entry) && entry.State == PlayerCacheState.Ready && entry.Data is not null)
                {
                    snapshots.Add(CreateSnapshot(entry.Data));
                    savedIds.Add(steamId);
                }
            }

            if (snapshots.Count == 0)
            {
                _modifiedCaches.Clear();
                return;
            }

            var accepted = _databaseWorker.TryEnqueue(new DbWorkItem(
                "SavePlayerBatch",
                null,
                cancellationToken => _playerRepository.UpdateRangeAsync(snapshots, cancellationToken)));

            if (!accepted)
            {
                _logger.LogWarning("Database queue rejected the batch save request, count: {count}", snapshots.Count);
                return;
            }

            foreach (var steamId in savedIds)
                _modifiedCaches.Remove(steamId);
        }

        private static Player CreateSnapshot(Player source) => new()
        {
            SteamId = source.SteamId,
            PlayerName = source.PlayerName,
            IpAddress = source.IpAddress,
            LastTimeConnect = source.LastTimeConnect,
            DefaultSkinModelPath = source.DefaultSkinModelPath,
            Volume = source.Volume,
            SaysoundVolume = source.SaysoundVolume,
            Language = source.Language
        };

        public void RemovePlayerCache(ulong steamId)
        {
            _modifiedCaches.Remove(steamId);
            if (_playerCaches.Remove(steamId, out var entry))
                entry.ClearPendingCallbacks();
        }

        public void SaveAndRemovePlayerCache(ulong steamId)
        {
            if (!_playerCaches.TryGetValue(steamId, out var entry))
                return;

            if (entry is { State: PlayerCacheState.Ready, Data: not null } && _modifiedCaches.Contains(steamId))
                SavePlayerCacheToDB(entry.Data);

            RemovePlayerCache(steamId);
        }

        public void ClearPlayerCaches()
        {
            foreach (var entry in _playerCaches.Values)
                entry.ClearPendingCallbacks();
            _playerCaches.Clear();
            _modifiedCaches.Clear();
        }
    }
}
