using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using VSBot.Classes;
using VSBot.Models;
using VSBot.Services.Interfaces;

namespace VSBot.Modules
{
    public class BotModule
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<BotModule> _logger;
        private readonly IGameStateService _gameStateService;
        private readonly IOptionsMonitor<Config> _config;

        public BotModule(ISwiftlyCore core, ILogger<BotModule> logger, IGameStateService gameStateService, IOptionsMonitor<Config> config)
        {
            _core = core;
            _logger = logger;
            _gameStateService = gameStateService;
            _config = config;
        }

        public void OnMapStart()
        {
            _logger.LogInformation("BotModule OnMapStart");

            StopBotMoving();
            _gameStateService.MapReady += AddSpecialBot;
        }

        private void StopBotMoving()
        {
            _logger.LogInformation("StopBotMoving");

            _core.Scheduler.NextWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("sw_cvar bot_stop 1");
            });
        }

        private void AddSpecialBot()
        {
            _gameStateService.MapReady -= AddSpecialBot;
            _logger.LogInformation("AddSpecialBot");

            var humanTeam = _gameStateService.HumanTeam;
            var botTeam = humanTeam == Team.CT ? Team.T : Team.CT;
            var hasAdded = _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[0]) == 1 &&
                _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[1]) == 1 &&
                _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[2]) == 1;

            if (!hasAdded)
            {
                KickBot();
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    _core.Engine.ExecuteCommand($"bot_add_{botTeam} {nameof(BotProfile.Difficulty.expert)} {BotProfile.Special[0]}");
                    _core.Engine.ExecuteCommand($"bot_add_{botTeam} {nameof(BotProfile.Difficulty.expert)} {BotProfile.Special[1]}");
                    _core.Engine.ExecuteCommand($"bot_add_{botTeam} {nameof(BotProfile.Difficulty.expert)} {BotProfile.Special[2]}");

                    if (_config.CurrentValue.IsDebug)
                    {
                        _core.Logger.LogInformation("bot_add_{botTeam} {difficulty} {botName}", botTeam, nameof(BotProfile.Difficulty.expert), BotProfile.Special[0]);
                        _core.Logger.LogInformation("bot_add_{botTeam} {difficulty} {botName}", botTeam, nameof(BotProfile.Difficulty.expert), BotProfile.Special[1]);
                        _core.Logger.LogInformation("bot_add_{botTeam} {difficulty} {botName}", botTeam, nameof(BotProfile.Difficulty.expert), BotProfile.Special[2]);
                    }
                });
            }
        }

        private void KickBot()
        {
            _logger.LogInformation("KickBot");
            _core.Scheduler.NextWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("bot_kick");
            });
        }
    }
}
