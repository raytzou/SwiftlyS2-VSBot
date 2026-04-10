using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class GameStateService : IGameStateService
    {
        public IReadOnlyDictionary<ulong, IPlayer> Players => _players;
        public int PlayerCount => _players.Count;
        public short CurrentRound
        {
            get
            {
                return (short)(_isWarmup ? 0 : _core.Game.MatchData.ActualRoundsPlayed + 1);
            }
        }
        public bool IsWarmup => _isWarmup;
        public bool IsBuyTime => _isBuyTime;

        private Dictionary<ulong, IPlayer> _players = [];
        private bool _isWarmup = false;
        private bool _isBuyTime = false;

        private readonly ISwiftlyCore _core;
        private readonly ILogger<GameStateService> _logger;

        public GameStateService(ILogger<GameStateService> logger, ISwiftlyCore core)
        {
            _logger = logger;
            _core = core;
        }

        public void AddPlayer(IPlayer player)
        {
            _players.Add(player.SteamID, player);
        }

        public void RemovePlayer(ulong SteamID)
        {
            _players.Remove(SteamID);
        }

        public void ClearPlayers()
        {
            _players.Clear();
        }

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

        public void OnBuyTimeStart()
        {
            if (_isBuyTime) return;

            _isBuyTime = true;
            _logger.LogInformation("Buy time started.");
        }

        public void OnBuyTimeEnd()
        {
            if (!_isBuyTime) return;

            _isBuyTime = false;
            _logger.LogInformation("Buy time ended.");
        }
    }
}
