namespace VSBot.Services.Interfaces
{
    public interface IGameStateService
    {
        public int PlayerCount { get; }
        public void UpdatePlayerCount(int playerCount);
    }
}
