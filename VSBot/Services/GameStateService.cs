using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class GameStateService : IGameStateService
    {
        public string CurrentMap => _currentMap;
        public int PlayerCount => _playerCount;

        private string _currentMap = "Unknown";
        private int _playerCount = 0;

        public void UpdateCurrentMap(string mapName)
        {
            if (string.IsNullOrEmpty(mapName))
                throw new ArgumentNullException("Attempted to set invalid map name.");

            _currentMap = mapName;
        }

        public void UpdatePlayerCount(int playerCount) => _playerCount = playerCount;
    }
}
