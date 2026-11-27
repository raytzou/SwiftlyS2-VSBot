using System.Runtime.CompilerServices;
using SwiftlyS2.Shared.Players;
using VSBot.Domain.Entities;

namespace VSBot.Services.Interfaces
{
    public enum PlayerCacheState
    {
        Loading,
        Ready,
        LoadFailed
    }

    public interface IPlayerDataService
    {
        void PreparePlayerCache(IPlayer player);

        /// <summary>
        /// Game thread only. Runs synchronously when ready, or once after the current load succeeds.
        /// Pending callbacks are discarded on load failure or cache removal. Use synchronous callbacks only.
        /// Exceptions are logged and isolated; persistent changes still require UpdatePlayerCache.
        /// </summary>
        void WhenPlayerCacheReady(ulong steamId, Action<Player> callback, [CallerMemberName] string caller = "");

        Player? GetPlayerCache(ulong steamId);

        PlayerCacheState? GetPlayerCacheState(ulong steamId);

        void UpdatePlayerCache(Player player);

        void SavePlayerCacheToDB(Player player);

        void SaveAllPlayerCachesToDB();

        void RemovePlayerCache(ulong steamId);

        void SaveAndRemovePlayerCache(ulong steamId);

        void ClearPlayerCaches();

        void WarmUp();

        void PrepareShutdown();
    }
}
