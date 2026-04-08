namespace VSBot.Services.Interfaces
{
    public interface IGameStateService
    {
        public string CurrentMap { get; }
        public int PlayerCount { get; }
        public void UpdateCurrentMap(string mapName);
        public void UpdatePlayerCount(int playerCount);
    }
}
