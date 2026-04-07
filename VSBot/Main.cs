using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Plugins;
using System.Reflection;
using VSBot.Commands.Interfaces;
using VSBot.Modules;

namespace VSBot
{
    [PluginMetadata(Author = "cynicat", Name = "VSBot Plugin", Id = "26.04.01", Version = "26.04.01")]
    public class Main : BasePlugin
    {
        private ServiceProvider? _serviceProvider;

        public Main(ISwiftlyCore core) : base(core)
        {

        }

        public override void Load(bool hotReload)
        {
            ServiceCollection services = new();

            RegisterServices(services);
            RegisterCommands(services);
            _serviceProvider = services.BuildServiceProvider();
            InitializeCommandModule();
            Core.Logger.LogInformation("Plugin Loaded.");
        }

        public override void Unload()
        {
            _serviceProvider?.Dispose();
        }

        private void RegisterServices(IServiceCollection services)
        {
            services.AddSwiftly(Core);
            services.AddTransient<CommandModule>();
        }

        private void InitializeCommandModule()
        {
            if (_serviceProvider is null)
                throw new InvalidOperationException("Service provider is null.");

            var command = _serviceProvider.GetRequiredService<CommandModule>();
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
    }
}
