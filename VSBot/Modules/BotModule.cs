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

        private int _level = 0;

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

            DisableBotMovement();
            _gameStateService.MapReady += AddSpecialBot;
        }

        public void OnWarmupEnd()
        {
            _logger.LogInformation("BotModule OnWarmupEnd");

            if (!_config.CurrentValue.IsDebug)
            {
                DisableCheats();
                EnableBotMovement();
            }

            SetDifficulty(0, 0);
            FillNormalBot();
        }

        private void DisableBotMovement()
        {
            _logger.LogInformation("DisableBotMovement");

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

        private void DisableCheats()
        {
            _logger.LogInformation("DisableCheats");
            _core.Scheduler.NextWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("sv_cheats 0");
            });
        }

        private void EnableBotMovement()
        {
            _logger.LogInformation("EnableBotMovement");
            _core.Scheduler.NextWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("sw_cvar bot_stop 0");
            });
        }

        private void SetDifficulty(int winStreak, int looseStreak)
        {
            if (winStreak >= 1 && _level < 7)
                _level++;
            else if (looseStreak >= 2 && _level > 1)
                _level--;
        }

        private void FillNormalBot()
        {
            _logger.LogInformation("FillNormalBot");

            var difficulty = _level switch
            {
                0 => BotProfile.Difficulty.easy,
                1 or 2 => BotProfile.Difficulty.normal,
                3 or 4 => BotProfile.Difficulty.hard,
                5 or 6 or 7 => BotProfile.Difficulty.expert,
                _ => BotProfile.Difficulty.easy,
            };
            var humanTeam = _gameStateService.HumanTeam;
            var botTeam = humanTeam == Team.CT ? Team.T : Team.CT;

            _core.Scheduler.NextWorldUpdate(() =>
            {
                for (int i = 1; i <= _config.CurrentValue.BotQuota - BotProfile.Special.Count; i++)
                {
                    string botName = $"\"[{BotProfile.Grade[_level]}]{BotProfile.NameGroup.Keys.ToList()[_level]}#{i:D2}\"";
                    var team = (botTeam == Team.CT) ? "ct" : "t";

                    _core.Engine.ExecuteCommand($"bot_add_{team} {difficulty} {botName}");

                    if (_config.CurrentValue.IsDebug)
                        _logger.LogInformation("bot_add_{team} {difficulty} {botName}", team, difficulty, botName);
                }
            });
        }
    }
}
