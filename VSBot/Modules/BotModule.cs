using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Players;
using System.Text.RegularExpressions;
using VSBot.Classes;
using VSBot.Models;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot.Modules
{
    public class BotModule
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<BotModule> _logger;
        private readonly IGameStateService _gameStateService;
        private readonly IOptionsMonitor<Config> _config;
        private readonly IScoreboard _scoreboard;

        private int _mapGeneration;
        private bool _mapActive;
        private CancellationTokenSource? _randomSpawnTimer;
        private CancellationTokenSource? _roundWork;
        private readonly Dictionary<ulong, CancellationTokenSource> _pickupTimers = new();
        private int _level = 2;
        private int _maxRespawnTimes = 0;
        private int _respawnTimes = 0;
        private readonly Regex NormalBotNameRegex = new(@"^\[(?<Grade>[^\]]+)\](?<Group>[^#]+)#(?<Num>\d{1,2})$");

        public BotModule(ISwiftlyCore core, ILogger<BotModule> logger, IGameStateService gameStateService, IOptionsMonitor<Config> config, IScoreboard scoreboard)
        {
            _core = core;
            _logger = logger;
            _gameStateService = gameStateService;
            _config = config;
            _scoreboard = scoreboard;
        }

        public void OnMapStart()
        {
            _logger.LogInformation("{moduleName} {methodName}", nameof(BotModule), nameof(OnMapStart));

            ClearMapResources();
            _mapActive = true;
            Initialize();
            DisableBotMovement();
            DisableRandomSpawn();
            _gameStateService.MapReady += RemoveKnife;
            _gameStateService.MapReady += AddSpecialBot;
        }

        public void ClearMapResources()
        {
            ClearRoundResources();
            ++_mapGeneration;
            _mapActive = false;
            _gameStateService.MapReady -= RemoveKnife;
            _gameStateService.MapReady -= AddSpecialBot;
        }

        private void ClearRoundResources()
        {
            CancelRandomSpawnTimer();
            _roundWork?.Cancel();
            _roundWork?.Dispose();
            _roundWork = null;
            foreach (var timer in _pickupTimers.Values) timer.Cancel();
            _pickupTimers.Clear();
        }

        public void ClearPlayerResources(ulong sessionId)
        {
            if (_pickupTimers.Remove(sessionId, out var timer)) timer.Cancel();
        }

        // Use for map startup work and callbacks retaining map-specific teams or player objects.
        private void NextMapWorldUpdate(Action action)
        {
            if (!_mapActive) return;
            var generation = _mapGeneration;
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_mapActive && generation == _mapGeneration) action();
            });
        }

        public void OnWarmupEnd()
        {
            _logger.LogInformation("{moduleName} {methodName}", nameof(BotModule), nameof(OnWarmupEnd));

            if (!_config.CurrentValue.IsDebug)
            {
                DisableCheats();
                EnableBotMovement();
            }

            SetDifficulty(_gameStateService.WinStreak, _gameStateService.LooseStreak);
            FillNormalBot();
            SetMaxRespawnTimes();
            DisableRandomSpawn();
        }

        public void OnRoundStart()
        {
            _logger.LogInformation("{moduleName} {methodName}", nameof(BotModule), nameof(OnRoundStart));

            ClearRoundResources();
            _roundWork = new CancellationTokenSource();
            DisableRandomSpawn();
            AllowAllBotsPickupWeapon();
            SetAllBotMoneyToZero();
            SetSpecialBotAttribute();
            EnableRandomSpawn();

            if (_gameStateService.CurrentRound > 1)
            {
                SetSpecialBotWeapon();
            }

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
                    _logger.LogWarning("{methodName}: Special bots are not added successfully", nameof(SetSpecialBotAttribute));
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
                if (!AreSpecialBotsAdded())
                {
                    _logger.LogWarning("{methodName}: Special bots are not added successfully", nameof(SetSpecialBotWeapon));
                    return;
                }

                SetBotWeapon(BotProfile.Special[0], ItemDefinitionIndex.Awp);
                SetBotWeapon(BotProfile.Special[1], ItemDefinitionIndex.M4a1Silencer);
                SetBotWeapon(BotProfile.Special[2], ItemDefinitionIndex.P90);
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

        public void OnRoundEnd()
        {
            ClearRoundResources();
            DisableRandomSpawn();
            SetMaxRespawnTimes();
            SetDifficulty(_gameStateService.WinStreak, _gameStateService.LooseStreak);

            if (_gameStateService.CurrentRound > 0)
            {
                SetDefaultWeapon();
                KickNormalBot();
                FillNormalBot();
            }
        }

        public void RespawnBot(IPlayer player)
        {
            ClearPlayerResources(player.SessionId);
            if (!player.IsValid || _roundWork is null) return;
            var roundToken = _roundWork.Token;
            var sessionId = player.SessionId;
            if (_gameStateService.IsRoundEnd)
                return;

            if (_respawnTimes < 1 || !player.IsFakeClient)
                return;

            var specialBots = BotProfile.Special.Values.ToHashSet();

            if (specialBots.Contains(player.Name))
                return;

            _respawnTimes--;
            _gameStateService.SetCurrentBotRespawnRemaining(_respawnTimes);
            _scoreboard.Update(_respawnTimes);
            NextMapWorldUpdate(() =>
            {
                if (roundToken.IsCancellationRequested || _gameStateService.IsRoundEnd) return;
                var currentPlayer = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
                if (currentPlayer is null || !currentPlayer.IsValid || !currentPlayer.IsFakeClient) return;
                AllowBotPickupWeapon(currentPlayer.Name);
                currentPlayer.Respawn();
                PreventBotPickupWeapon(currentPlayer.Name);
            });
        }

        private void DisableBotMovement()
        {
            _logger.LogInformation("{methodName}", nameof(DisableBotMovement));

            NextMapWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("sw_cvar bot_stop 1");
            });
        }

        private void AddSpecialBot()
        {
            _gameStateService.MapReady -= AddSpecialBot;
            _logger.LogInformation("{methodName}", nameof(AddSpecialBot));

            if (AreSpecialBotsAdded())
                return;

            var humanTeam = _gameStateService.HumanTeam;
            var botTeam = humanTeam == Team.CT ? Team.T : Team.CT;

            KickBot();
            NextMapWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand($"bot_add_{botTeam} {nameof(BotProfile.Difficulty.expert)} {BotProfile.Special[0]}");
                _core.Engine.ExecuteCommand($"bot_add_{botTeam} {nameof(BotProfile.Difficulty.expert)} {BotProfile.Special[1]}");
                _core.Engine.ExecuteCommand($"bot_add_{botTeam} {nameof(BotProfile.Difficulty.expert)} {BotProfile.Special[2]}");

                if (_config.CurrentValue.IsDebug)
                {
                    NextMapWorldUpdate(LogMapStartupState);
                    _logger.LogInformation("bot_add_{botTeam} {difficulty} {botName}", botTeam, nameof(BotProfile.Difficulty.expert), BotProfile.Special[0]);
                    _logger.LogInformation("bot_add_{botTeam} {difficulty} {botName}", botTeam, nameof(BotProfile.Difficulty.expert), BotProfile.Special[1]);
                    _logger.LogInformation("bot_add_{botTeam} {difficulty} {botName}", botTeam, nameof(BotProfile.Difficulty.expert), BotProfile.Special[2]);
                }
            });
        }

        private void LogMapStartupState()
        {
            if (!_config.CurrentValue.IsDebug) return;
            var specialBotCount = _core.PlayerManager.GetBots().Count(bot => BotProfile.Special.Values.Contains(bot.Name));
            _logger.LogInformation(
                "Map startup state: map={Map}, humanTeam={HumanTeam}, mp_humanteam={HumanTeamCvar}, bot_stop={BotStop}, warmup={Warmup}, specialBots={SpecialBots}/3.",
                _core.Engine.GlobalVars.MapName, _gameStateService.HumanTeam,
                _core.ConVar.FindAsString("mp_humanteam")?.ValueAsString,
                _core.ConVar.FindAsString("bot_stop")?.ValueAsString,
                _gameStateService.IsWarmup, specialBotCount);
        }

        private void KickBot()
        {
            _logger.LogInformation("{methodName}", nameof(KickBot));
            NextMapWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("bot_kick");
            });
        }

        private void DisableCheats()
        {
            _logger.LogInformation("{methodName}", nameof(DisableCheats));
            _core.Scheduler.NextWorldUpdate(() =>
            {
                _core.Engine.ExecuteCommand("sv_cheats 0");
            });
        }

        private void EnableBotMovement()
        {
            _logger.LogInformation("{methodName}", nameof(EnableBotMovement));
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

            _gameStateService.SetCurrentBotLevel(_level + 1);
            _logger.LogInformation("{methodName}: level: {level}", nameof(SetDifficulty), _level);
        }

        private void FillNormalBot()
        {
            _logger.LogInformation("{methodName}", nameof(FillNormalBot));

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

            NextMapWorldUpdate(() =>
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
            _logger.LogInformation("{methodName}: bot {botName}, amount {amount}", nameof(SetBotMoney), botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(SetBotMoney), botName);
                    return;
                }

                if (bot.Controller.InGameMoneyServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null", nameof(SetBotMoney), botName);
                    return;
                }

                bot.Controller.InGameMoneyServices.StartAccount = 0;
                bot.Controller.InGameMoneyServices.Account = 0;

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} StartAccount={startAccount} Account={account}", nameof(SetBotMoney), botName, bot.Controller.InGameMoneyServices.StartAccount, bot.Controller.InGameMoneyServices.Account);
                }
            });
        }

        private void SetBotWeapon(string botName, ItemDefinitionIndex index)
        {
            _logger.LogInformation("{methodName}: bot {botName}, item {item}", nameof(SetBotWeapon), botName, index);

            var bot = _core.PlayerManager
                .GetBots()
                .FirstOrDefault(f => f.Name == botName);

            if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
            {
                _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(SetBotWeapon), botName);
                return;
            }

            RemoveBotWeapon(botName);
            GiveBotWeapon(botName, index);
        }

        private void RemoveBotWeapon(string botName)
        {
            _logger.LogInformation("{methodName}: bot {botName}", nameof(RemoveBotWeapon), botName);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(RemoveBotWeapon), botName);
                    return;
                }

                if (bot.PlayerPawn.ItemServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} item service is null", nameof(RemoveBotWeapon), botName);
                    return;
                }

                bot.PlayerPawn.ItemServices.RemoveItems();
                bot.PlayerPawn.ItemServicesUpdated();


                if (bot.PlayerPawn.WeaponServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} weapon service is null", nameof(RemoveBotWeapon), botName);
                    return;
                }

                foreach (var weapon in bot.PlayerPawn.WeaponServices.MyValidWeapons)
                {
                    bot.PlayerPawn.WeaponServices.RemoveWeapon(weapon);
                }

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} weapon count={count}", nameof(RemoveBotWeapon), botName, bot.PlayerPawn.WeaponServices.MyWeapons.Count);
                }
            });
        }

        private void GiveBotWeapon(string botName, ItemDefinitionIndex index)
        {
            _logger.LogInformation("{methodName}: bot {botName} item {index}", nameof(GiveBotWeapon), botName, index);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(GiveBotWeapon), botName);
                    return;
                }

                if (bot.PlayerPawn.ItemServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} item service is null", nameof(GiveBotWeapon), botName);
                    return;
                }

                var weaponEntityName = WeaponUtility.GetEntityNameByIndex(index);

                bot.PlayerPawn.ItemServices.GiveItem(weaponEntityName);

                if (_config.CurrentValue.IsDebug)
                {
                    if (bot.PlayerPawn.WeaponServices is null)
                    {
                        _logger.LogWarning("{methodName}: bot {botName} weapon service is null, skip checking weapon count", nameof(GiveBotWeapon), botName);
                        return;
                    }

                    _logger.LogInformation("{methodName}: bot {botName} weapon count={count}", nameof(GiveBotWeapon), botName, bot.PlayerPawn.WeaponServices.MyWeapons.Count);
                }
            });
        }

        private void SetBotScore(string botName, int amount)
        {
            _logger.LogInformation("{methodName}: bot {botName} amount {amount}", nameof(SetBotScore), botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(SetBotScore), botName);
                    return;
                }

                bot.Controller.Score = amount;
                bot.Controller.ScoreUpdated();

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} Score={score}", nameof(SetBotScore), botName, bot.Controller.Score);
                }
            });
        }

        private void SetBotHealth(string botName, int amount)
        {
            _logger.LogInformation("{methodName}: bot {botName} amount {amount}", nameof(SetBotHealth), botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(SetBotHealth), botName);
                    return;
                }

                bot.PlayerPawn.Health = amount;
                bot.PlayerPawn.HealthUpdated();

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} Health={health}", nameof(SetBotHealth), botName, bot.PlayerPawn.Health);
                }
            });
        }

        private void SetBotArmor(string botName, int amount)
        {
            _logger.LogInformation("{methodName}: bot {botName} amount {amount}", nameof(SetBotArmor), botName, amount);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                var bot = _core.PlayerManager
                    .GetBots()
                    .FirstOrDefault(f => f.Name == botName);

                if (bot is null || !bot.IsValid || bot.PlayerPawn is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} is null or invalid", nameof(SetBotArmor), botName);
                    return;
                }

                NextMapWorldUpdate(() =>
                {
                    bot.PlayerPawn.ArmorValue = amount;
                    bot.PlayerPawn.ArmorValueUpdated();
                });

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} ArmorValue={armor}", nameof(SetBotArmor), botName, bot.PlayerPawn.ArmorValue);
                }

                if (bot.PlayerPawn.ItemServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} item service is null", nameof(SetBotArmor), botName);
                    return;
                }

                NextMapWorldUpdate(() =>
                {
                    bot.PlayerPawn.ItemServices.HasHelmet = true;
                    bot.PlayerPawn.ItemServices.HasHelmetUpdated();
                });

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} HasHelmet={helmet}", nameof(SetBotArmor), botName, bot.PlayerPawn.ItemServices.HasHelmet);
                }
            });
        }

        private void PreventBotPickupWeapon(string botName)
        {
            _logger.LogInformation("{methodName}: bot {botName}", nameof(PreventBotPickupWeapon), botName);

            if (_roundWork is null || _gameStateService.IsRoundEnd) return;
            var roundToken = _roundWork.Token;
            var player = _core.PlayerManager.GetBots().FirstOrDefault(bot => bot.Name == botName);
            if (player is null || !player.IsValid || player.PlayerPawn is not { IsValid: true }) return;
            var sessionId = player.SessionId;
            var pawnHandle = player.Controller.PlayerPawn;
            ClearPlayerResources(sessionId);
            var freezeTime = _core.ConVar.Find<int>("mp_freezetime")!.Value;

            var timer = _core.Scheduler.DelayBySeconds(freezeTime, () =>
            {
                _pickupTimers.Remove(sessionId);
                if (roundToken.IsCancellationRequested || _gameStateService.IsRoundEnd) return;
                var bot = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
                if (bot is null || !bot.IsValid || !bot.IsFakeClient
                    || bot.PlayerPawn is not { IsValid: true }
                    || !bot.Controller.PlayerPawn.Equals(pawnHandle)) return;

                if (bot.PlayerPawn.WeaponServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} weapon service is null", nameof(PreventBotPickupWeapon), botName);
                    return;
                }

                bot.PlayerPawn.WeaponServices.PreventWeaponPickup = true;

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} PreventWeaponPickup={PreventWeaponPickup}", nameof(PreventBotPickupWeapon), botName, bot.PlayerPawn.WeaponServices.PreventWeaponPickup);
                }
            });
            _pickupTimers[sessionId] = timer;
            // Keep native map cancellation in addition to round/player cleanup.
            _core.Scheduler.StopOnMapChange(timer);
        }

        private void AllowBotPickupWeapon(string botName)
        {
            _logger.LogInformation("{methodName}: bot {botName}", nameof(AllowBotPickupWeapon), botName);
            var player = _core.PlayerManager.GetBots().FirstOrDefault(bot => bot.Name == botName);
            if (player is null || !player.IsValid || _roundWork is null) return;
            var sessionId = player.SessionId;
            var roundToken = _roundWork.Token;
            ClearPlayerResources(sessionId);
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (roundToken.IsCancellationRequested || _gameStateService.IsRoundEnd) return;
                var bot = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
                if (bot is null || !bot.IsValid || !bot.IsFakeClient || bot.PlayerPawn is not { IsValid: true }) return;

                if (bot.PlayerPawn.WeaponServices is null)
                {
                    _logger.LogWarning("{methodName}: bot {botName} weapon service is null", nameof(AllowBotPickupWeapon), botName);
                    return;
                }

                bot.PlayerPawn.WeaponServices.PreventWeaponPickup = false;

                if (_config.CurrentValue.IsDebug)
                {
                    _logger.LogInformation("{methodName}: bot {botName} AllowBotPickupWeapon={AllowBotPickupWeapon}", nameof(AllowBotPickupWeapon), botName, bot.PlayerPawn.WeaponServices.PreventWeaponPickup);
                }
            });
        }

        private void RemoveKnife()
        {
            _gameStateService.MapReady -= RemoveKnife;

            var humanTeam = _gameStateService.HumanTeam;
            var botTeam = humanTeam == Team.CT ? Team.T : Team.CT;

            if (botTeam == Team.CT)
            {
                _core.Engine.ExecuteCommand("mp_ct_default_melee \"\"");
            }
            else
            {
                _core.Engine.ExecuteCommand("mp_t_default_melee \"\"");
            }
        }

        private void SetDefaultWeapon()
        {
            var humanTeam = _gameStateService.HumanTeam;
            var botTeam = humanTeam == Team.CT ? Team.T : Team.CT;

            if (botTeam == Team.CT)
            {
                _core.Engine.ExecuteCommand("mp_ct_default_secondary \"\"");
                _core.Engine.ExecuteCommand("mp_ct_default_primary weapon_m4a1");
            }
            else
            {
                _core.Engine.ExecuteCommand("mp_t_default_secondary \"\"");
                _core.Engine.ExecuteCommand("mp_t_default_primary weapon_ak47");
            }
        }

        private void SetMaxRespawnTimes()
        {
            _maxRespawnTimes = (_level < 3) ? 100 : (_level == 4) ? 120 : 150;
            _respawnTimes = _maxRespawnTimes;
            _gameStateService.SetCurrentBotMaxRespawnTimes(_maxRespawnTimes);
            _gameStateService.SetCurrentBotRespawnRemaining(_respawnTimes);
            _scoreboard.Update(_respawnTimes);
        }

        private void EnableRandomSpawn()
        {
            CancelRandomSpawnTimer();
            var buyTime = _core.ConVar.Find<float>("mp_buytime")!.Value;
            _randomSpawnTimer = _core.Scheduler.DelayBySeconds(buyTime, () =>
            {
                _randomSpawnTimer = null;
                _core.Engine.ExecuteCommand("mp_randomspawn 1");
            });

            _core.Scheduler.StopOnMapChange(_randomSpawnTimer);
        }

        private void CancelRandomSpawnTimer()
        {
            _randomSpawnTimer?.Cancel();
            _randomSpawnTimer = null;
        }

        private void DisableRandomSpawn()
        {
            CancelRandomSpawnTimer();
            _core.Engine.ExecuteCommand("mp_randomspawn 0");
        }

        private void KickNormalBot()
        {
            foreach (var bot in _core.PlayerManager.GetBots())
            {
                var match = NormalBotNameRegex.Match(bot.Name);

                if (match.Success)
                {
                    _core.Engine.ExecuteCommand($"kick {bot.Name}");
                }
            }

            _core.Engine.ExecuteCommand("bot_quota 3");
        }

        private void Initialize()
        {
            _level = 2;
            _maxRespawnTimes = 0;
            _respawnTimes = 0;
        }

        private bool AreSpecialBotsAdded() => _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[0]) == 1 &&
            _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[1]) == 1 &&
            _core.PlayerManager.GetBots().Count(bot => bot.Name == BotProfile.Special[2]) == 1;
    }
}
