using SwiftlyS2.Shared.Events;

namespace VSBot.Services.Interfaces
{
    public interface IModelService
    {
        void Initialize();
        void Precache(IOnPrecacheResourceEvent @event);
        void SetPlayerModel();
    }
}
