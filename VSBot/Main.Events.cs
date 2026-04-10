using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;
using VSBot.Classes;
using VSBot.Services.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot
{
    public partial class Main
    {
        [GameEventHandler(HookMode.Post)]
        public HookResult PlayerConnectFullHandler(EventPlayerConnectFull @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid) return HookResult.Continue;

            if (!player.IsFakeClient)
            {
                var gameState = GetRequiredService<IGameStateService>();

                gameState.AddPlayer(player);
                SendConnectedMessage(gameState.Players.Values.ToList());
                Core.Logger.LogInformation("{client} has connected at {DT}, IP: {ipAddress}, SteamID: {steamID}", player.Name, DateTime.Now, player.IPAddress, player.SteamID);
            }

            return HookResult.Continue;

            void SendConnectedMessage(List<IPlayer> players)
            {
                foreach (var thePlayer in players)
                {
                    thePlayer.SendChat($" {ChatColors.Lime}{player.Name} {Utility.GetTeamColorByPlayer(thePlayer)}has connected to the server.");
                }
            }
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult PlayerDisconnectHandler(EventPlayerDisconnect @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid) return HookResult.Continue;

            if (!player.IsFakeClient)
            {
                var gameState = GetRequiredService<IGameStateService>();

                gameState.RemovePlayer(player.SteamID);
                Core.Logger.LogInformation("{client} has disconnected at {DT}", player.Name, DateTime.Now);
            }

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult RoundAnnounceWarmupHandler(EventRoundAnnounceWarmup @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnWarmupStarted();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult WarmupEndHandler(EventWarmupEnd @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnWarmupEnded();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult RoundStartHandler(EventRoundStart @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnBuyTimeStart();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult BuytimeEndedHandler(EventBuytimeEnded @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnBuyTimeEnd();

            return HookResult.Continue;
        }
    }
}
