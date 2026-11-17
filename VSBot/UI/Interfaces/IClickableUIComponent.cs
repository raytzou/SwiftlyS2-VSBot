using SwiftlyS2.Shared.Events;

namespace VSBot.UI.Interfaces
{
    internal interface IClickableUIComponent
    {
        void OnClicked(IOnCustomHudClickedEvent @event);
    }
}
