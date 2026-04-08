namespace VSBot.Services.Interfaces
{
    public interface IGameStateService
    {
        public int PlayerCount { get; }
        public short CurrentRound { get; }
        public bool IsWarmup { get; }
        public void UpdatePlayerCount(int playerCount);
        public void OnWarmupStarted();
        public void OnWarmupEnded();
    }
}
