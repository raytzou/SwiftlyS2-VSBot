using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using VSBot.Models;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class ModelService : IModelService
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<ModelService> _logger;
        private readonly IOptionsMonitor<Config> _config;

        public ModelService(ISwiftlyCore core, ILogger<ModelService> logger, IOptionsMonitor<Config> config)
        {
            _core = core;
            _logger = logger;
            _config = config;
        }

        private Dictionary<string, ModelInfo>? _modelInfos;

        public void Initialize()
        {
            var gameRoot = _core.GameDirectory;
            var modelInfoFileName = _config.CurrentValue.ModelInfoFile;

            if (string.IsNullOrEmpty(modelInfoFileName))
            {
                throw new InvalidOperationException("Model Info File is not set.");
            }

            var modelInfoFile = Path.Join(gameRoot, modelInfoFileName);
            if (!File.Exists(modelInfoFile))
            {
                throw new InvalidOperationException($"Cannot find the {modelInfoFileName} in game root folder.");
            }

            _modelInfos = new();

            var lines = File.ReadAllLines(modelInfoFile)
                .Select(line => line.Split(["//"], StringSplitOptions.None)[0].Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line));

            foreach (var line in lines)
            {
                var split = line.Split(',');
                var modelName = split[0];
                var modelPath = split[1];
                var armPath = split.Length > 2 ? split[2] : null;
                var meshGroupIndex = split.Length > 3 ? split[3] : null;

                _modelInfos.Add(modelName, new ModelInfo());
                _modelInfos[modelName].ModelPath = modelPath;
                if (!string.IsNullOrEmpty(armPath))
                    _modelInfos[modelName].ArmPath = armPath;
                else
                    _modelInfos[modelName].ArmPath = null;
                if (!string.IsNullOrEmpty(meshGroupIndex))
                    _modelInfos[modelName].MeshGroupIndex = int.Parse(meshGroupIndex);
                else
                    _modelInfos[modelName].MeshGroupIndex = null;
            }

            _logger.LogInformation("Custom player models have been initialized, count: {count}", _modelInfos.Count);
        }

        public void Precache(IOnPrecacheResourceEvent @event)
        {
            if (_modelInfos is null)
            {
                throw new InvalidOperationException("Custom models are not initialized.");
            }

            var modelInfosIterator = _modelInfos
                .Select(s => s.Value);
            var modelPath = modelInfosIterator
                .Select(s => s.ModelPath)
                .ToHashSet();
            var armPath = modelInfosIterator
                .Select(s => s.ArmPath)
                .Where(w => w is not null)
                .ToHashSet();

            foreach (var model in modelPath)
                @event.AddItem(model);
            foreach (var arm in armPath)
                @event.AddItem(arm!);

            _logger.LogInformation("Custom models have been precached.");
        }

        public void SetPlayerModel()
        {
            throw new NotImplementedException();
        }
    }
}
