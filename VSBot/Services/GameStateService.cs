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
        public bool IsRoundEnd => _isRoundEnd;
        public Team HumanTeam
        {
            get
            {
                var mapName = _core.Engine.GlobalVars.MapName.ToString();

                switch (mapName[..3])
                {
                    case "cs_":
                        return Team.CT;
                    case "de_":
                        return Team.T;
                    default:
                        _logger.LogWarning("Cannot identify the category of map: {mapName}", mapName);
                        return Team.T;
                }
            }
        }

        public event Action? MapReady;

        private Dictionary<ulong, IPlayer> _players = [];
        private bool _isWarmup = false;
        private bool _isBuyTime = false;
        private bool _isRoundEnd = false;

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
            _logger.LogInformation("Warmup started");
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
            _logger.LogInformation("Buy time started");
        }

        public void OnBuyTimeEnd()
        {
            if (!_isBuyTime) return;

            _isBuyTime = false;
            _logger.LogInformation("Buy time ended");
        }

        public void OnRoundStart()
        {
            if (!_isRoundEnd) return;

            _isRoundEnd = false;
            _logger.LogInformation("Round started");
        }

        public void OnRoundEnd()
        {
            if (_isRoundEnd) return;

            _isRoundEnd = true;
            _logger.LogInformation("Round ended");
        }

        public void OnMapStart()
        {
            _core.Scheduler.NextWorldUpdate(() => MapReady?.Invoke());
        }
    }
}
