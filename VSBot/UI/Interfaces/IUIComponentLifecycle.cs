using SwiftlyS2.Shared.SchemaDefinitions;

namespace VSBot.UI.Interfaces
{
    internal interface IUIComponentLifecycle
    {
        CCSCustomHudLayout? Layout { get; }
        void Initialize();
        void Unload();
    }
}
