using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using VSBot.UI.Interfaces;

namespace VSBot.Modules
{
    public class UIModule
    {
        private readonly ISwiftlyCore _core;
        private readonly IUIComponentLifecycle[] _components;
        private readonly Dictionary<nint, IClickableUIComponent> _clickableComponents = new();
        private bool _registered;
        private bool _initialized;

        public UIModule(ISwiftlyCore core, IMotd motd, IScoreboard scoreboard, ISaysoundLanguage saysoundLanguage)
        {
            _core = core;
            _components = [(IUIComponentLifecycle)motd, (IUIComponentLifecycle)scoreboard, (IUIComponentLifecycle)saysoundLanguage];
        }

        public void Register()
        {
            if (_registered) return;

            _core.Event.OnCustomHudClicked += OnClicked;
            _registered = true;
        }

        public void Initialize()
        {
            if (_initialized) return;

            try
            {
                foreach (var component in _components)
                    component.Initialize();

                foreach (var component in _components)
                {
                    var layout = component.Layout;
                    if (layout is null || !layout.IsValid)
                        throw new InvalidOperationException($"{component.GetType().Name} did not create a valid HUD.");

                    if (component is IClickableUIComponent clickable)
                        _clickableComponents.Add(layout.Address, clickable);
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to initialize UI components.");
                OnMapUnload();
            }
        }

        public void OnMapUnload()
        {
            _clickableComponents.Clear();
            try
            {
                foreach (var component in _components)
                {
                    try
                    {
                        component.Unload();
                    }
                    catch (Exception ex)
                    {
                        _core.Logger.LogError(ex, "Failed to unload UI component {Component}.", component.GetType().Name);
                    }
                }
            }
            finally
            {
                _initialized = false;
            }
        }

        public void Unload()
        {
            try
            {
                if (_registered)
                {
                    _core.Event.OnCustomHudClicked -= OnClicked;
                    _registered = false;
                }
            }
            finally
            {
                OnMapUnload();
            }
        }

        private void OnClicked(IOnCustomHudClickedEvent @event)
        {
            if (_clickableComponents.TryGetValue(@event.CustomHudLayout.Address, out var component))
                component.OnClicked(@event);
        }
    }
}
