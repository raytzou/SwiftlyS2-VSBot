using SwiftlyS2.Shared.Commands;

namespace VSBot.Commands.Interfaces
{
    public interface ICommand
    {
        public abstract void ExecuteCommand(ICommandContext context);
    }
}
