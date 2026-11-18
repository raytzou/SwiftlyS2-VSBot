using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VSBot.Models;
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
        private readonly IOptionsMonitor<Config> _config;
        private int _mapGeneration;
        private bool _registered;
        private bool _initialized;

        public UIModule(ISwiftlyCore core, IMotd motd, IScoreboard scoreboard, ISaysoundLanguage saysoundLanguage, IOptionsMonitor<Config> config)
        {
            _core = core;
            _config = config;
            _components = [(IUIComponentLifecycle)motd, (IUIComponentLifecycle)scoreboard, (IUIComponentLifecycle)saysoundLanguage];
        }

        public void Register()
        {
            if (_registered) return;

            _core.Event.OnCustomHudClicked += OnClicked;
            _core.Event.OnStartupServer += OnStartupServer;
            _registered = true;
        }

        private void OnStartupServer()
        {
            var generation = ++_mapGeneration;
            if (_config.CurrentValue.IsDebug)
                _core.Logger.LogInformation("UI startup scheduled for map {Map}, generation {Generation}.", _core.Engine.GlobalVars.MapName, generation);

            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (!_registered || generation != _mapGeneration) return;
                Initialize();
            });
        }

        private void Initialize()
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
                if (_config.CurrentValue.IsDebug)
                    _core.Logger.LogInformation("UI initialized for map {Map}, generation {Generation}: {Count} HUDs.", _core.Engine.GlobalVars.MapName, _mapGeneration, _components.Length);
            }
            catch (Exception ex)
            {
                _core.Logger.LogError(ex, "Failed to initialize UI components.");
                OnMapUnload();
            }
        }

        public void OnMapUnload()
        {
            ++_mapGeneration;
            if (_config.CurrentValue.IsDebug)
                _core.Logger.LogInformation("UI cleanup, generation {Generation}, initialized {Initialized}.", _mapGeneration, _initialized);
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
            if (_registered)
            {
                _core.Event.OnCustomHudClicked -= OnClicked;
                _core.Event.OnStartupServer -= OnStartupServer;
                _registered = false;
            }

            OnMapUnload();
        }

        private void OnClicked(IOnCustomHudClickedEvent @event)
        {
            if (_initialized && @event.CustomHudLayout.IsValid
                && _clickableComponents.TryGetValue(@event.CustomHudLayout.Address, out var component)
                && component is IUIComponentLifecycle lifecycle && lifecycle.Layout is { IsValid: true })
                component.OnClicked(@event);
        }
    }
}
