using SwiftlyS2.Shared.Players;

namespace VSBot.Services.Interfaces
{
    public interface IGameStateService
    {
        public IReadOnlyDictionary<ulong, IPlayer> Players { get; }
        public int PlayerCount { get; }
        public short CurrentRound { get; }
        public bool IsWarmup { get; }
        public bool IsBuyTime { get; }
        public void AddPlayer(IPlayer player);
        public void RemovePlayer(ulong SteamID);
        public void ClearPlayers();
        public void OnWarmupStarted();
        public void OnWarmupEnded();
        public void OnBuyTimeStart();
        public void OnBuyTimeEnd();
    }
}
