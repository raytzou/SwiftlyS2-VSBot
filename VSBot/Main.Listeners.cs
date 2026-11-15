using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Misc;
using VSBot.Services.Interfaces;

namespace VSBot
{
    public partial class Main
    {
        private HookResult SaysoundListener(int playerId, string text, bool teamonly)
        {
            var textArray = text.Split(' ');

            if (textArray.Length > 2)
                return HookResult.Continue;

            var saysoundService = GetRequiredService<ISaysoundService>();

            if (!saysoundService.IsInitialized)
            {
                Core.Logger.LogError("Saysound is not initialized");
                return HookResult.Continue;
            }

            var saysoundKeyword = textArray[0];
            var pitchText = textArray.Length < 2 ? string.Empty : textArray[1];
            var isSaysound = saysoundService.IsSaysound(saysoundKeyword);

            if (!isSaysound)
                return HookResult.Continue;

            var isPitchText = saysoundService.IsPitchText(pitchText);

            if (textArray.Length == 2 && !isPitchText)
                return HookResult.Continue;

            saysoundService.BroadcastSaysound(playerId, saysoundKeyword, pitchText);
            saysoundService.PlaySaysound(saysoundKeyword, pitchText);

            return HookResult.Stop;
        }
    }
}
