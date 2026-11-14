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
