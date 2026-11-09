using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Models;
using VSBot.Services.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot.Commands
{
    public class Info : IInfo
    {
        private readonly ISwiftlyCore _core;
        private readonly IGameStateService _gameStateService;
        private readonly IOptionsMonitor<Config> _config;

        public Info(ISwiftlyCore core, IGameStateService gameStateService, IOptionsMonitor<Config> config)
        {
            _core = core;
            _gameStateService = gameStateService;
            _config = config;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;
            var hostname = _core.ConVar.Find<string>("hostname")?.Value;
            var currentMap = _core.Engine.GlobalVars.MapName;
            var playerCount = _gameStateService.PlayerCount;
            var maxClients = _core.Engine.GlobalVars.MaxClients;
            var currentRound = _gameStateService.CurrentRound;
            var availableRounds = _config.CurrentValue.MaxRounds - 1;
            var currentLevel = _gameStateService.CurrentBotLevel;
            var botRemaining = _gameStateService.CurrentBotRespawnRemaining;
            var botMaxRespawnTimes = _gameStateService.CurrentBotMaxRespawnTimes;

            context.Reply($" {ChatColors.Default}----- {ChatColors.Purple}{hostname} {ChatColors.Default}-----");
            context.Reply($" {Utility.GetTeamColorByPlayer(player)}Server local time: {ChatColors.Red}{DateTime.Now}");
            context.Reply($" {Utility.GetTeamColorByPlayer(player)}Current map: {ChatColors.Lime}{currentMap}");
            context.Reply($" {Utility.GetTeamColorByPlayer(player)}Player: {ChatColors.Yellow}{playerCount}{ChatColors.Grey}/{maxClients}");

            if (_gameStateService.CurrentRound > 0)
            {
                context.Reply($" {Utility.GetTeamColorByPlayer(player)}Round: {ChatColors.Yellow}{currentRound}{ChatColors.Grey}/{availableRounds}");
                context.Reply($" {Utility.GetTeamColorByPlayer(player)}Level: {ChatColors.Red}{currentLevel}{ChatColors.Grey}/{BotProfile.MaxLevel}");
                context.Reply($" {Utility.GetTeamColorByPlayer(player)}Bot remaining: {ChatColors.Purple}{botRemaining}{ChatColors.Grey}/{botMaxRespawnTimes}");
            }

            context.Reply($" {ChatColors.Default}----------");
        }
    }
}
