using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;
using VSBot.Services.Interfaces;

namespace VSBot.Commands
{
    public class Slay : ISlay
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<Slay> _logger;
        private readonly IPlayerService _playerService;

        public Slay(ISwiftlyCore core, ILogger<Slay> logger, IPlayerService playerService)
        {
            _core = core;
            _logger = logger;
            _playerService = playerService;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            if (context.Args.Length < 1 || context.Args.Length > 1)
            {
                context.Reply("[sw] Usage: sw_slay <target>");
                return;
            }

            var client = context.Sender;
            var target = context.Args[0];
            var adminName = client?.Name ?? "Console";

            if (target == "@all")
            {
                foreach (var player in _core.PlayerManager.GetAllPlayers())
                {
                    if (player is null || !player.IsValid || player.PlayerPawn is null) continue;

                    player.PlayerPawn.CommitSuicide(true, true);
                }

                _core.PlayerManager.SendChat("Admin slew all players");
                _logger.LogInformation("{admin} slew all players at {DT}", adminName, DateTime.Now);

                return;
            }
            else if (target == "@ct")
            {
                foreach (var player in _core.PlayerManager.GetCT())
                {
                    if (player is null || !player.IsValid || player.PlayerPawn is null) continue;

                    player.PlayerPawn.CommitSuicide(true, true);
                }

                _core.PlayerManager.SendChat($"Admin slew all CT players");
                _logger.LogInformation("{admin} slew all CT players at {DT}", adminName, DateTime.Now);

                return;
            }
            else if (target == "@t")
            {
                foreach (var player in _core.PlayerManager.GetT())
                {
                    if (player is null || !player.IsValid || player.PlayerPawn is null) continue;

                    player.PlayerPawn.CommitSuicide(true, true);
                }

                _core.PlayerManager.SendChat($"Admin slew all T players");
                _logger.LogInformation("{admin} slew all T players at {DT}", adminName, DateTime.Now);

                return;
            }

            var targetName = _playerService.GetNameByKeyword(target);

            if (string.IsNullOrEmpty(targetName))
            {
                context.Reply("[sw] Target not found.");

                return;
            }

            var targetPlayer = _core.PlayerManager
                .GetAllPlayers()
                .FirstOrDefault(p => p.Name == targetName);

            if (targetPlayer is null || !targetPlayer.IsValid || targetPlayer.PlayerPawn is null)
            {
                context.Reply("[sw] Cannot slay the target");
                _logger.LogWarning("Target {targetName} is null or invalid", targetName);

                return;
            }

            targetPlayer.PlayerPawn.CommitSuicide(true, true);
            context.Reply($"[sw] You slew {targetName}");
            _core.PlayerManager.SendChat($"Admin slew {targetName}");
        }
    }
}
