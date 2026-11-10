using SwiftlyS2.Shared.Events;

namespace VSBot.UI.Interfaces
{
    public interface IUIComponent
    {
        public abstract void Initialize();
        public abstract void Open(int playerId);
        public abstract void Close(int playerId);
        public abstract void OnClicked(IOnCustomHudClickedEvent @event);
    }
}
