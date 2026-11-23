using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;

namespace VSBot.Services.Interfaces
{
    public interface IModelService
    {
        void Initialize();
        void Precache(IOnPrecacheResourceEvent @event);
        void SetPlayerModel(IPlayer player, string modelName);
    }
}
