using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;
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

        public void OnRoundStart()
        {
            _logger.LogInformation("BotModule OnRoundStart");

            AllowAllBotsPickupWeapon();
            SetAllBotMoneyToZero();
            SetSpecialBotAttribute();
            SetSpecialBotWeapon();
            PreventAllBotsPickupWeapon();

            void SetAllBotMoneyToZero()
            {
                var bots = _core.PlayerManager
                    .GetBots()
                    .ToList();

                foreach (var bot in bots)
                {
                    SetBotMoney(bot.Name, 0);
                }
            }

            void SetSpecialBotAttribute()
            {
                if (!AreSpecialBotsAdded())
                {
                    _logger.LogWarning("SetSpecialBotAttribute: Special bots are not added successfully");
                    return;
                }

                SetBotScore(BotProfile.Special[0], 999);
                SetBotScore(BotProfile.Special[1], 888);
                SetBotScore(BotProfile.Special[2], 777);
                SetBotArmor(BotProfile.Special[0], 100);
                SetBotArmor(BotProfile.Special[1], 100);
                SetBotArmor(BotProfile.Special[2], 100);

                if (_gameStateService.CurrentRound > 1)
                    SetBotHealth(BotProfile.Special[2], 500);
            }

            void SetSpecialBotWeapon()
            {
                if (_gameStateService.CurrentRound > 1)
                {
                    SetBotWeapon(BotProfile.Special[0], ItemDefinitionIndex.Awp);
                    SetBotWeapon(BotProfile.Special[1], ItemDefinitionIndex.M4a1Silencer);
                    SetBotWeapon(BotProfile.Special[2], ItemDefinitionIndex.P90);
                }
            }

            void PreventAllBotsPickupWeapon()
            {
                var bots = _core.PlayerManager
                    .GetBots()
                    .ToList();

                foreach (var bot in bots)
                {
                    PreventBotPickupWeapon(bot.Name);
                }
            }

            void AllowAllBotsPickupWeapon()
            {
                var bots = _core.PlayerManager
                    .GetBots()
                    .ToList();

                foreach (var bot in bots)
                {
                    AllowBotPickupWeapon(bot.Name);
                }
            }
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

            if (!AreSpecialBotsAdded())
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

            _logger.LogInformation("SetDifficulty, level: {level}", _level);
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

        private void SetBotMoney(string botName, int amount)
        {
            _logger.LogInformation("SetBotMoney: bot {botName}, amount {amount}", botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid)
                {
                    _logger.LogWarning("SetBotMoney: bot {botName} is null or invalid", botName);
                    return;
                }

                if (bot.Controller.InGameMoneyServices is null)
                {
                    _logger.LogWarning("SetBotMoney: bot {botName} is null", botName);
                    return;
                }

                bot.Controller.InGameMoneyServices.StartAccount = 0;
                bot.Controller.InGameMoneyServices.Account = 0;

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("SetBotMoney: bot {botName} StartAccount={startAccount} Account={account}", botName, bot.Controller.InGameMoneyServices.StartAccount, bot.Controller.InGameMoneyServices.Account);
                }
            });
        }

        private void SetBotWeapon(string botName, ItemDefinitionIndex index)
        {
            _logger.LogInformation("SetBotWeapon: bot {botName}, item {item}", botName, index);

            var bot = _core.PlayerManager
                .GetBots()
                .FirstOrDefault(f => f.Name == botName);

            if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
            {
                _logger.LogWarning("SetBotWeapon: bot {botName} is null or invalid", botName);
                return;
            }

            RemoveBotWeapon(botName);
            GiveBotWeapon(botName, index);
        }

        private void RemoveBotWeapon(string botName)
        {
            _logger.LogInformation("RemoveBotWeapon: bot {botName}", botName);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("RemoveBotWeapon: bot {botName} is null or invalid", botName);
                    return;
                }

                if (bot.PlayerPawn.ItemServices is null)
                {
                    _logger.LogWarning("RemoveBotWeapon: bot {botName} item service is null", botName);
                    return;
                }

                bot.PlayerPawn.ItemServices.RemoveItems();
                bot.PlayerPawn.ItemServicesUpdated();

                if (_config.CurrentValue.IsDebug)
                {
                    if (bot.PlayerPawn.WeaponServices is null)
                    {
                        _logger.LogWarning("RemoveBotWeapon: bot {botName} weapon service is null, skip checking weapon count", botName);
                        return;
                    }

                    _logger.LogInformation("RemoveBotWeapon: bot {botName} weapon count={count}", botName, bot.PlayerPawn.WeaponServices.MyWeapons.Count);
                }
            });
        }

        private void GiveBotWeapon(string botName, ItemDefinitionIndex index)
        {
            _logger.LogInformation("GiveBotWeapon: bot {botName} item {index}", botName, index);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("GiveBotWeapon: bot {botName} is null or invalid", botName);
                    return;
                }

                if (bot.PlayerPawn.ItemServices is null)
                {
                    _logger.LogWarning("GiveBotWeapon: bot {botName} item service is null", botName);
                    return;
                }

                var weaponEntityName = WeaponUtility.GetEntityNameByIndex(index);

                bot.PlayerPawn.ItemServices.GiveItem(weaponEntityName);

                if (_config.CurrentValue.IsDebug)
                {
                    if (bot.PlayerPawn.WeaponServices is null)
                    {
                        _logger.LogWarning("GiveBotWeapon: bot {botName} weapon service is null, skip checking weapon count", botName);
                        return;
                    }

                    _logger.LogInformation("GiveBotWeapon: bot {botName} weapon count={count}", botName, bot.PlayerPawn.WeaponServices.MyWeapons.Count);
                }
            });
        }

        private void SetBotScore(string botName, int amount)
        {
            _logger.LogInformation("SetBotScore: bot {botName} amount {amount}", botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid)
                {
                    _logger.LogWarning("SetBotScore: bot {botName} is null or invalid", botName);
                    return;
                }

                bot.Controller.Score = amount;
                bot.Controller.ScoreUpdated();

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("SetBotScore: bot {botName} Score={score}", botName, bot.Controller.Score);
                }
            });
        }

        private void SetBotHealth(string botName, int amount)
        {
            _logger.LogInformation("SetBotHealth: bot {botName} amount {amount}", botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("SetBotHealth: bot {botName} is null or invalid", botName);
                    return;
                }

                bot.PlayerPawn.Health = amount;
                bot.PlayerPawn.HealthUpdated();

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("SetBotHealth: bot {botName} Health={health}", botName, bot.PlayerPawn.Health);
                }
            });
        }

        private void SetBotArmor(string botName, int amount)
        {
            _logger.LogInformation("SetBotArmor: bot {botName} amount {amount}", botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("SetBotArmor: bot {botName} is null or invalid", botName);
                    return;
                }

                bot.PlayerPawn.ArmorValue = amount;
                bot.PlayerPawn.ArmorValueUpdated();

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("SetBotArmor: bot {botName} ArmorValue={armor}", botName, bot.PlayerPawn.ArmorValue);
                }
            });
        }

        private void PreventBotPickupWeapon(string botName)
        {
            _logger.LogInformation("PreventBotPickupWeapon: bot {botName}", botName);

            var buyTime = _core.ConVar.Find<float>("mp_buytime")!.Value;

            _core.Scheduler.DelayBySeconds(buyTime, () =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("PreventBotPickupWeapon: bot {botName} is null or invalid", botName);
                    return;
                }

                if (bot.PlayerPawn.WeaponServices is null)
                {
                    _logger.LogWarning("PreventBotPickupWeapon: bot {botName} weapon service is null", botName);
                    return;
                }

                bot.PlayerPawn.WeaponServices.PreventWeaponPickup = true;

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("PreventBotPickupWeapon: bot {botName} PreventWeaponPickup={PreventWeaponPickup}", botName, bot.PlayerPawn.WeaponServices.PreventWeaponPickup);
                }
            });
        }

        private void AllowBotPickupWeapon(string botName)
        {
            _logger.LogInformation("AllowBotPickupWeapon: bot {botName}", botName);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("AllowBotPickupWeapon: bot {botName} is null or invalid", botName);
                    return;
                }

                if (bot.PlayerPawn.WeaponServices is null)
                {
                    _logger.LogWarning("AllowBotPickupWeapon: bot {botName} weapon service is null", botName);
                    return;
                }

                bot.PlayerPawn.WeaponServices.PreventWeaponPickup = false;

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("AllowBotPickupWeapon: bot {botName} AllowBotPickupWeapon={AllowBotPickupWeapon}", botName, bot.PlayerPawn.WeaponServices.PreventWeaponPickup);
                }
            });
        }

        private bool AreSpecialBotsAdded() => _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[0]) == 1 &&
            _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[1]) == 1 &&
            _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[2]) == 1;
    }
}
