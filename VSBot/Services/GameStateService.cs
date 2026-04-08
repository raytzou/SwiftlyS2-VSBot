using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class GameStateService : IGameStateService
    {
        public int PlayerCount => _playerCount;

        private int _playerCount = 0;

        public void UpdatePlayerCount(int playerCount) => _playerCount = playerCount;
    }
}
