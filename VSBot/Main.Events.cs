using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using VSBot.Services.Interfaces;

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
            }

            return HookResult.Continue;
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
    }
}
