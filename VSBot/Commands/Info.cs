using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Services.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot.Commands
{
    public class Info : IInfo
    {
        private readonly ISwiftlyCore _core;
        private readonly IGameStateService _gameStateService;

        public Info(ISwiftlyCore core, IGameStateService gameStateService)
        {
            _core = core;
            _gameStateService = gameStateService;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            context.Reply($" {ChatColors.Default}----- {ChatColors.Purple}{_core.ConVar.Find<string>("hostname")?.Value} {ChatColors.Default}-----");
            context.Reply($" {Utility.GetTeamColorByPlayer(player)}Server local time: {ChatColors.Red}{DateTime.Now}");
            context.Reply($" {Utility.GetTeamColorByPlayer(player)}Current map: {ChatColors.Lime}{_gameStateService.CurrentMap}");
            context.Reply($" {ChatColors.Default}----------");
        }
    }
}
