using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;

namespace VSBot.Services.Interfaces
{
    public interface IModelService
    {
        List<string> ModelNames { get; }

        string? GetImageKey(string modelName);

        void Initialize();
        void Precache(IOnPrecacheResourceEvent @event);
        void SetPlayerModel(IPlayer player, string modelName);
        void SetPlayerDefaultModelPath(IPlayer player);
        void SetPlayerModelByPath(IPlayer player, string modelPath);
    }
}
