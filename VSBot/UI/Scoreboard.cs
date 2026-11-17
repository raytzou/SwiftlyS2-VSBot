using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SchemaDefinitions;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot.UI
{
    public class Scoreboard : IScoreboard, IUIComponentLifecycle
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

        CCSCustomHudLayout? IUIComponentLifecycle.Layout => _scoreboard;

        void IUIComponentLifecycle.Unload()
        {
            var layout = _scoreboard;
            _scoreboard = null;

            if (layout is null || !layout.IsValid) return;

            layout.Despawn();
        }

        public void Close(int playerId)
        {

        }

        void IUIComponentLifecycle.Initialize()
        {
            _scoreboard = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
            _scoreboard.StrLayout = "panorama/layout/custom_game/scoreboard.xml";
            _scoreboard.StrLayoutUpdated();
            _scoreboard.DispatchSpawn();
        }

        public void Open(int playerId)
        {

        }

        public void Update(int botRemaining)
        {
            if (_scoreboard is null || !_scoreboard.IsValid) return;

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
