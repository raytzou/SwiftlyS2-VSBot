using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Plugins;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Models;
using VSBot.Modules;
using VSBot.Services;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot
{
    [PluginMetadata(Author = "cynicat", Name = "VSBot Plugin", Id = "vsbot", Version = "26.11.20")]
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
            RegisterByConvention(services);
            _serviceProvider = services.BuildServiceProvider();
            InitializeCommandModule();
            Utility.Initialize();
            RegisterEvents();
            GetRequiredService<IPatchService>().ApplyAllAutoPatches();
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
            services.AddTransient<UIModule>();
            services.AddSingleton<BotModule>();
            services.AddSingleton<INativeMemoryService, NativeMemoryService>();
            services.AddSingleton<IPatchService, PatchService>();
            services.AddSingleton<IGameStateService, GameStateService>();
            services.AddSingleton<IPlayerService, PlayerService>();
        }

        private void InitializeCommandModule()
        {
            var command = GetRequiredService<CommandModule>();
            command.Initialize();
        }

        private void RegisterByConvention(IServiceCollection services)
        {
            var originalCount = services.Count;

            services.Scan(scan => scan
                .FromAssemblyOf<Main>()
                .AddClasses(c => c.Where(t => t.Namespace == "VSBot.Commands" && typeof(ICommand).IsAssignableFrom(t)), publicOnly: false)
                    .AsImplementedInterfaces(i => i.Namespace == "VSBot.Commands.Interfaces" && i != typeof(ICommand))
                    .WithTransientLifetime()
                .AddClasses(c => c.Where(t => t.Namespace == "VSBot.UI" && typeof(IUIComponent).IsAssignableFrom(t)), publicOnly: false)
                    .AsImplementedInterfaces(i => i.Namespace == "VSBot.UI.Interfaces" && i != typeof(IUIComponent))
                    .WithSingletonLifetime());

            foreach (var descriptor in services.Skip(originalCount))
            {
                Core.Logger.LogInformation(
                    "Registered: {Service} -> {Implementation} ({Lifetime})",
                    descriptor.ServiceType.Name,
                    descriptor.ImplementationType?.Name ?? "Factory",
                    descriptor.Lifetime
                );
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
