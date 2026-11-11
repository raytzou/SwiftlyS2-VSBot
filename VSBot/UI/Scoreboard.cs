using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SchemaDefinitions;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot.UI
{
    public class Scoreboard : IScoreboard
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<Scoreboard> _logger;
        private readonly IGameStateService _gameStateService;

        public Scoreboard(ISwiftlyCore core, ILogger<Scoreboard> logger, IGameStateService gameStateService)
        {
            _core = core;
            _logger = logger;
            _gameStateService = gameStateService;
        }

        private CCSCustomHudLayout? _scoreboard;

        public void Close(int playerId)
        {

        }

        public void Initialize()
        {
            _scoreboard = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
            _scoreboard.StrLayout = "panorama/layout/custom_game/scoreboard.xml";
            _scoreboard.StrLayoutUpdated();
            _scoreboard.DispatchSpawn();
            _core.Event.OnCustomHudClicked += OnClicked;
        }

        public void OnClicked(IOnCustomHudClickedEvent @event)
        {
            return;
        }

        public void Open(int playerId)
        {

        }

        public void Update(int botRemaining)
        {
            if (_scoreboard is null)
            {
                _logger.LogError("Scoreboard is null.");
                throw new InvalidOperationException("Scoreboard is null. Is ModuleUI initialized?");
            }

            foreach (var playerPair in _gameStateService.Players)
            {
                var player = playerPair.Value;

                if (player is null || !player.IsValid || player.PlayerPawn is null || !player.PlayerPawn.IsValid)
                    continue;

                _scoreboard.SetDialogVariableStringForPlayer(
                    player.PlayerID,
                    "bot_respawns",
                    "remaining",
                    botRemaining.ToString()
                );
            }
        }
    }
}
