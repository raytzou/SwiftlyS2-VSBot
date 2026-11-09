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
        public bool IsRoundEnd { get; }
        public Team HumanTeam { get; }
        public int WinStreak { get; }
        public int LooseStreak { get; }
        public int CurrentBotLevel { get; }
        public int CurrentBotMaxRespawnTimes { get; }
        public int CurrentBotRespawnRemaining { get; }

        public event Action? MapReady;

        public void AddPlayer(IPlayer player);
        public void RemovePlayer(ulong SteamID);
        public void ClearPlayers();
        public void OnWarmupStarted();
        public void OnWarmupEnded();
        public void OnBuyTimeStart();
        public void OnBuyTimeEnd();
        public void OnRoundStart();
        public void OnRoundEnd();
        public void OnMapStart();
        public void CalculateStreak(byte winTeam);
        public void SetCurrentBotLevel(int level);
        public void SetCurrentBotMaxRespawnTimes(int maxRespawnTimes);
        public void SetCurrentBotRespawnRemaining(int respawnRemaining);
    }
}
