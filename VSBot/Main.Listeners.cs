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
            var saysounds = saysoundService.Saysound;
            var saysoundKeyword = textArray[0];
            var pitchText = textArray.Length < 2 ? string.Empty : textArray[1];

            if (!saysounds.TryGetValue(saysoundKeyword, out var _))
                return HookResult.Continue;

            var isPitchText = saysoundService.PitchMapper.TryGetValue(pitchText, out var _) || pitchText == "r";

            if (textArray.Length == 2 && !isPitchText)
                return HookResult.Continue;

            try
            {
                ProcessSaysound(playerId, saysoundKeyword, pitchText);
            }
            catch (NullReferenceException) // Saysound is not initialized
            {
                return HookResult.Continue;
            }

            return HookResult.Stop;
        }

        private void ProcessSaysound(int playerId, string saysoundKeyword, string pitchText)
        {
            var saysoundService = GetRequiredService<ISaysoundService>();
            var saysound = saysoundService.Saysound[saysoundKeyword];
            var pitch = pitchText == "r" ?
                Random.Shared.Next(75, 150) / 100f : string.IsNullOrEmpty(pitchText) ?
                1f : saysoundService.PitchMapper[pitchText];

            saysoundService.BroadcastSaysound(playerId, saysoundKeyword, pitchText);
            saysoundService.PlaySaysound(saysound.SoundEvent, pitch);
        }
    }
}
