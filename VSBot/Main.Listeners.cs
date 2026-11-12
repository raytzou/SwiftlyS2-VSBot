using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Translation;
using VSBot.Classes;
using VSBot.Services.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot
{
    public partial class Main
    {
        private static readonly IReadOnlyDictionary<string, float> _pitchMapper = new Dictionary<string, float>()
        {
            { "qq", 1.5f },
            { "q", 1.3f },
            { "f", 1.15f },
            { "s", 0.85f },
            { "d", 0.75f }
        };

        private HookResult SaysoundListener(int playerId, string text, bool teamonly)
        {
            var textArray = text.Split(' ');

            if (textArray.Length > 2)
                return HookResult.Continue;

            var saysoundService = GetRequiredService<ISaysoundService>();
            var saysounds = saysoundService.Saysound;
            var saysoundKeyword = textArray[0];
            var pitchText = textArray.Length < 2 ? string.Empty : textArray[1];

            if (!saysounds.TryGetValue(saysoundKeyword, out var _))
                return HookResult.Continue;

            var isPitchText = _pitchMapper.TryGetValue(pitchText, out var _) || pitchText == "r";

            if (textArray.Length == 2 && !isPitchText)
                return HookResult.Continue;

            ProcessSaysound(playerId, saysoundKeyword, pitchText);

            return HookResult.Stop;
        }

        private void ProcessSaysound(int playerId, string saysoundKeyword, string pitchText)
        {
            var saysoundService = GetRequiredService<ISaysoundService>();

            BroadcastSaysound(playerId, saysoundKeyword, pitchText);
            saysoundService.PlaySaysound();
        }

        private void BroadcastSaysound(int playerId, string saysoundKeyword, string pitchText)
        {
            var sender = Core.PlayerManager.GetPlayer(playerId);
            var gameStateService = GetRequiredService<IGameStateService>();
            var players = gameStateService.Players;
            var saysoundService = GetRequiredService<ISaysoundService>();
            var saysound = saysoundService.Saysound[saysoundKeyword];

            foreach (var playerPair in players)
            {
                var player = playerPair.Value;

                if (player is null || !player.IsValid)
                    continue;

                var playerLanguage = player.PlayerLanguage;
                var content = saysound.Content;

                if (playerLanguage.Equals(Language.ChineseTW))
                    content = saysound.TWContent;

                if (playerLanguage.Equals(Language.Japanese))
                    content = saysound.JPContent;

                var displayText = _pitchMapper.TryGetValue(pitchText, out var _) || pitchText == "r" ?
                    $"{saysoundKeyword} {pitchText}" : saysoundKeyword;

                player.SendChat($" {Utility.GetTeamColorByPlayer(player)}{sender?.Name}: {ChatColors.Grey}[{displayText}] {ChatColors.Green}{content}");
            }
        }
    }
}
