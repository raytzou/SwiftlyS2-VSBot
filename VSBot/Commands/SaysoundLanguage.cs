using SwiftlyS2.Shared.Commands;

namespace VSBot.Commands
{
    public class SaysoundLanguage : Interfaces.ISaysoundLanguage
    {
        private readonly UI.Interfaces.ISaysoundLanguage _saysoundLanguageUI;

        public SaysoundLanguage(UI.Interfaces.ISaysoundLanguage saysoundLanguageUI)
        {
            _saysoundLanguageUI = saysoundLanguageUI;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            if (player is null || !player.IsValid)
                return;

            _saysoundLanguageUI.Open(player.PlayerID);
        }
    }
}
