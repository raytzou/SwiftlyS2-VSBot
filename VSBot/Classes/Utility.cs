using SwiftlyS2.Shared.Players;
using static SwiftlyS2.Shared.Helper;

namespace VSBot.Classes
{
    public static class Utility
    {
        public static IEnumerable<string> AllMaps => _mapsInPhysicalDirectory.Concat(_mapsFromWorkshop);

        private static List<string> _mapsFromWorkshop = [];
        private static List<string> _mapsInPhysicalDirectory = [];

        public static void Initialize()
        {
            InitializeMaps();
        }

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

        public static bool IsWorkshopMap(string mapName)
            => _mapsFromWorkshop.Contains(mapName);

        private static void InitializeMaps()
        {
            var dir = Main.SwiftlyCore.CSGODirectory.Trim("/csgo");
            var mapListPath = Path.Join(dir, "maplist.txt");
            if (!File.Exists(mapListPath)) throw new InvalidOperationException("Cannot find maplist.txt in root folder.");

            _mapsFromWorkshop = File.ReadAllLines(mapListPath).ToList();

            string mapFolderPath = Path.Join(dir, "csgo", "maps");

            _mapsInPhysicalDirectory = Directory.GetFiles(mapFolderPath)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(mapName =>
                    !string.IsNullOrEmpty(mapName) &&
                    !mapName.Contains("vanity") &&
                    !mapName.Contains("workshop_preview") &&
                    !mapName.Contains("graphics_settings") &&
                    !mapName.Contains("lobby_mapveto")).ToList()!;
        }
    }
}
