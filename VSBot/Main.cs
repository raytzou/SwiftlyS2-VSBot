using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Plugins;
using System.Reflection;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Models;
using VSBot.Modules;
using VSBot.Services;
using VSBot.Services.Interfaces;

namespace VSBot
{
    [PluginMetadata(Author = "cynicat", Name = "VSBot Plugin", Id = "vsbot", Version = "26.05.01")]
    public partial class Main : BasePlugin
    {
        public static ISwiftlyCore SwiftlyCore => _serviceProvider?.GetRequiredService<ISwiftlyCore>()
            ?? throw new InvalidOperationException("Service provider is null.");

        private static ServiceProvider? _serviceProvider;

        public Main(ISwiftlyCore core) : base(core) { }

        public override void Load(bool hotReload)
        {
            ServiceCollection services = new();

            RegisterServices(services);
            RegisterCommands(services);
            _serviceProvider = services.BuildServiceProvider();
            InitializeCommandModule();
            Utility.Initialize();
            RegisterEvents();
            Core.Configuration.InitializeJsonWithModel<Config>("config.jsonc", "Main")
                .Configure(builder =>
                {
                    builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true);
                });
            Core.Logger.LogInformation("Plugin Loaded.");
        }

        public override void Unload()
        {
            UnregisterEvents();
            UnhookGameEvents();
            _serviceProvider?.Dispose();
        }

        private void RegisterServices(IServiceCollection services)
        {
            services.AddSwiftly(Core).AddOptionsWithValidateOnStart<Config>().BindConfiguration("Main");
            services.AddTransient<CommandModule>();
            services.AddSingleton<IGameStateService, GameStateService>();
            services.AddSingleton<IPlayerService, PlayerService>();
        }

        private void InitializeCommandModule()
        {
            var command = GetRequiredService<CommandModule>();
            command.Initialize();
        }

        private void RegisterCommands(IServiceCollection services)
        {
            var commandTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.Namespace == "VSBot.Commands"
                         && !t.IsInterface
                         && !t.IsAbstract
                         && typeof(ICommand).IsAssignableFrom(t));

            foreach (var commandType in commandTypes)
            {
                var interfaces = commandType.GetInterfaces()
                    .Where(i => i.Namespace == "VSBot.Commands.Interfaces"
                             && i != typeof(ICommand));

                foreach (var @interface in interfaces)
                {
                    services.AddTransient(@interface, commandType);
                    Core.Logger.LogInformation($"Registered command: {@interface.Name} -> {commandType.Name}");
                }
            }
        }

        private void RegisterEvents()
        {
            Core.Event.OnMapLoad += OnMapLoad;
        }

        private void UnregisterEvents()
        {
            Core.Event.OnMapLoad -= OnMapLoad;
        }

        private void UnhookGameEvents()
        {
            Core.GameEvent.UnhookPost<EventPlayerConnectFull>();
            Core.GameEvent.UnhookPost<EventPlayerDisconnect>();
            Core.GameEvent.UnhookPost<EventRoundAnnounceWarmup>();
            Core.GameEvent.UnhookPost<EventWarmupEnd>();
            Core.GameEvent.UnhookPost<EventRoundStart>();
            Core.GameEvent.UnhookPost<EventRoundEnd>();
            Core.GameEvent.UnhookPost<EventBuytimeEnded>();
            Core.GameEvent.UnhookPost<EventPlayerDeath>();
        }

        private T GetRequiredService<T>() where T : notnull
        {
            if (_serviceProvider is null)
                throw new InvalidOperationException("Service provider is null.");
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}
