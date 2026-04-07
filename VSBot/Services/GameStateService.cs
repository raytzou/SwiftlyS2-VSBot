using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class GameStateService : IGameStateService
    {
        public string CurrentMap => _currentMap;

        private string _currentMap = "Unknown";

        public void UpdateCurrentMap(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                throw new ArgumentNullException("Attempted to set invalid map name.");

            _currentMap = mapName;
        }
    }
}
