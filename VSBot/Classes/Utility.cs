using SwiftlyS2.Shared.Players;
using static SwiftlyS2.Shared.Helper;

namespace VSBot.Classes
{
    public static class Utility
    {
        public static string GetTeamColorByPlayer(IPlayer? player)
        {
            return GetTeamColor(player?.Controller.Team);
        }

        public static string GetTeamColor(Team? team)
        {
            return team switch
            {
                Team.None => ChatColors.White,
                Team.Spectator => ChatColors.LightPurple,
                Team.CT => ChatColors.LightBlue,
                Team.T => ChatColors.Orange,
                _ => ChatColors.Default
            };
        }
    }
}
