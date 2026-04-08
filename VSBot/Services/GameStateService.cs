using Microsoft.Extensions.Logging;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class GameStateService : IGameStateService
    {
        public int PlayerCount => _playerCount;
        public short CurrentRound
        {
            get
            {
                return (short)(_isWarmup ? 0 : Main.SwiftlyCore.Game.MatchData.ActualRoundsPlayed + 1);
            }
        }
        public bool IsWarmup => _isWarmup;

        private int _playerCount = 0;
        private bool _isWarmup = false;

        private readonly ILogger<GameStateService> _logger;

        public GameStateService(ILogger<GameStateService> logger)
        {
            _logger = logger;
        }

        public void UpdatePlayerCount(int playerCount) => _playerCount = playerCount;

        public void OnWarmupStarted()
        {
            if (_isWarmup) return;

            _isWarmup = true;
            _logger.LogInformation("Warmup started.");
        }

        public void OnWarmupEnded()
        {
            if (!_isWarmup) return;

            _isWarmup = false;
            _logger.LogInformation("Warmup ended");
        }
    }
}
