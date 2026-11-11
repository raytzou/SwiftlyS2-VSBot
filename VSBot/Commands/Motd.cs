using SwiftlyS2.Shared.Commands;

namespace VSBot.Commands
{
    public class Motd : Interfaces.IMotd
    {
        private readonly UI.Interfaces.IMotd _motdUI;

        public Motd(UI.Interfaces.IMotd motdUI)
        {
            _motdUI = motdUI;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            if (player is null || !player.IsValid)
                return;

            _motdUI.Open(player.PlayerID);
        }
    }
}
