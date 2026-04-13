using SwiftlyS2.Shared.Commands;
using VSBot.Classes;
using VSBot.Commands.Interfaces;

namespace VSBot.Commands
{
    public class Maps : IMaps
    {
        public void ExecuteCommand(ICommandContext context)
        {
            foreach (var map in Utility.AllMaps)
            {
                context.Reply(map);
            }
        }
    }
}
