using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot.Commands
{
    public class Models : IModels
    {
        private readonly IModelSelect _modelSelect;

        public Models(IModelSelect modelSelect)
        {
            _modelSelect = modelSelect;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            if (player is not { IsValid: true })
                return;

            _modelSelect.Open(player.PlayerID);
        }
    }
}
