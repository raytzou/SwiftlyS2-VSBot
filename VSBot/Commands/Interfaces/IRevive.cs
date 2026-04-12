namespace VSBot.Commands.Interfaces
{
    public interface IRevive : ICommand
    {
        public void OnPlayerDisconnect(ulong steamID);
        public void OnRoundEnd();
    }
}
