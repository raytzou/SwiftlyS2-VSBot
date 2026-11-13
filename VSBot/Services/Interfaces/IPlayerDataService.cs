using SwiftlyS2.Shared.Players;
using VSBot.Domain.Entities;

namespace VSBot.Services.Interfaces
{
    public interface IPlayerDataService
    {
        void PreparePlayerCache(IPlayer player);

        Player? GetPlayerCache(ulong steamId);

        void UpdatePlayerCache(Player player);

        void SavePlayerCacheToDB(Player player);

        void SaveAllPlayerCachesToDB();

        void RemovePlayerCache(ulong steamId);

        void SaveAndRemovePlayerCache(ulong steamId);

        void ClearPlayerCaches();
    }
}
