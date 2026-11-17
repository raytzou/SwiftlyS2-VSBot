namespace VSBot.UI.Interfaces
{
    public interface IUIComponent
    {
        public abstract void Open(int playerId);
        public abstract void Close(int playerId);
    }
}
