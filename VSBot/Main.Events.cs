using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Players;

namespace VSBot
{
    public partial class Main
    {
        private readonly Dictionary<ulong, IPlayer> _players = [];

        [GameEventHandler(HookMode.Post)]
        public HookResult PlayerConnectFullHandler(EventPlayerConnectFull @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid) return HookResult.Continue;

            if (!player.IsFakeClient)
            {
                _players.Add(player.SteamID, player);
                UpdatePlayerCount();
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
                _players.Remove(player.SteamID);
                UpdatePlayerCount();
            }

            return HookResult.Continue;
        }
    }
}
