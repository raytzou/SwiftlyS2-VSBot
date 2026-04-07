namespace VSBot.Services.Interfaces
{
    public interface IGameStateService
    {
        public string CurrentMap { get; }
        public void UpdateCurrentMap(string mapName);
    }
}
