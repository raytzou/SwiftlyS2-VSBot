using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Plugins;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Domain.Repositories.Interfaces;
using VSBot.Infrastructure;
using VSBot.Models;
using VSBot.Modules;
using VSBot.Services;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot
{
    [PluginMetadata(Author = "cynicat", Name = "VSBot Plugin", Id = "vsbot", Version = "26.11.30")]
    public partial class Main : BasePlugin
    {
        public static ISwiftlyCore SwiftlyCore => _serviceProvider?.GetRequiredService<ISwiftlyCore>()
            ?? throw new InvalidOperationException("Service provider is null.");

        private static ServiceProvider? _serviceProvider;

        private HashSet<Guid> _listeners = new();

        public Main(ISwiftlyCore core) : base(core) { }

        public override void Load(bool hotReload)
        {
            ServiceCollection services = new();

            RegisterServices(services);
            AddDbContext(services);
            RegisterByConvention(services);
            _serviceProvider = services.BuildServiceProvider();
            InitializeConfig();
            GetRequiredService<IDatabaseWorker>().Start();
            GetRequiredService<IPlayerDataService>().WarmUp();
            InitializeCommandModule();
            Utility.Initialize();
            GetRequiredService<UIModule>().Register();
            RegisterEvents();
            RegisterListeners();
            GetRequiredService<IPatchService>().ApplyAllAutoPatches();
            GetRequiredService<ISaysoundService>().Initialize();
            Core.Logger.LogInformation("Plugin Loaded.");
        }

        public override void Unload()
        {
            UnregisterEvents();
            UnhookGameEvents();
            UnregisterListeners();

            var provider = _serviceProvider;
            if (provider is null) return;

            CancelMapInitialization();
            provider.GetRequiredService<UIModule>().Unload();

            var logger = Core.Logger;
            var worker = provider.GetRequiredService<IDatabaseWorker>();

            try
            {
                provider.GetRequiredService<IPlayerDataService>().PrepareShutdown();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to prepare player data for shutdown.");
            }

            var drained = worker.Shutdown(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(1));

            _serviceProvider = null;

            if (drained)
            {
                provider.Dispose();
                return;
            }

            logger.LogWarning("Database worker is still running, the service provider will be disposed after it completes.");
            DisposeProviderAfterWorker(provider, worker, logger);
        }

        private static void DisposeProviderAfterWorker(ServiceProvider provider, IDatabaseWorker worker, ILogger logger)
        {
            _ = worker.Completion.ContinueWith(task =>
            {
                try
                {
                    _ = task.Exception;
                    provider.Dispose();
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to dispose the service provider after the database worker completed.");
                }
            }, TaskScheduler.Default);
        }

        private void RegisterServices(IServiceCollection services)
        {
            services.AddSwiftly(Core).AddOptionsWithValidateOnStart<Config>().BindConfiguration("Main");
            services.AddTransient<CommandModule>();
            services.AddSingleton<UIModule>();
            services.AddSingleton<BotModule>();
            services.AddSingleton<INativeMemoryService, NativeMemoryService>();
            services.AddSingleton<IPatchService, PatchService>();
            services.AddSingleton<IGameStateService, GameStateService>();
            services.AddSingleton<IPlayerService, PlayerService>();
            services.AddSingleton<ISaysoundService, SaysoundService>();
            services.AddSingleton<IPlayerDataService, PlayerDataService>();
            services.AddSingleton<IDatabaseWorker, DatabaseWorker>();
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
                    .AsImplementedInterfaces(i => i.Namespace == "VSBot.UI.Interfaces" && i != typeof(IUIComponent)
                        && i != typeof(IUIComponentLifecycle) && i != typeof(IClickableUIComponent))
                    .WithSingletonLifetime()
                .AddClasses(c => c.Where(t => t.Namespace == "VSBot.Domain.Repositories" && t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IRepository<,>))), publicOnly: false)
                    .AsImplementedInterfaces(i => i.Namespace == "VSBot.Domain.Repositories.Interfaces" && !(i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRepository<,>)))
                    .WithTransientLifetime());

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
            Core.Event.OnStartupServer += OnStartupServer;
            Core.Event.OnMapUnload += OnMapUnload;
            Core.Event.OnPrecacheResource += OnPrecacheResource;
        }

        private void RegisterListeners()
        {
            _listeners.Add(Core.Command.HookClientChat(SaysoundListener));
        }

        private void UnregisterEvents()
        {
            Core.Event.OnMapLoad -= OnMapLoad;
            Core.Event.OnStartupServer -= OnStartupServer;
            Core.Event.OnMapUnload -= OnMapUnload;
            Core.Event.OnPrecacheResource -= OnPrecacheResource;
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

        private void UnregisterListeners()
        {
            foreach (var listener in _listeners)
            {
                Core.Command.UnregisterCommand(listener);
            }
        }

        private void AddDbContext(ServiceCollection services)
        {
            services.AddDbContextFactory<VSBotDbContext>((sp, options) =>
            {
                var connectionString = sp.GetRequiredService<IOptionsMonitor<Config>>().CurrentValue.ConnectionString;

                if (string.IsNullOrEmpty(connectionString))
                    throw new InvalidOperationException("Connection String is not set.");

                options.UseSqlServer(connectionString, sql => sql.CommandTimeout(10));
            });
        }

        private void InitializeConfig()
        {
            Core.Configuration.InitializeJsonWithModel<Config>("config.jsonc", "Main")
                .Configure(builder =>
                {
                    builder.AddJsonFile("config.jsonc", optional: false, reloadOnChange: true);
                });
        }

        private T GetRequiredService<T>() where T : notnull
        {
            if (_serviceProvider is null)
                throw new InvalidOperationException("Service provider is null.");
            return _serviceProvider.GetRequiredService<T>();
        }
    }
}
