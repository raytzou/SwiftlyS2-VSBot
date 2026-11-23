using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
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

        public List<string> ModelNames => _modelInfos?.Keys.ToList() ??
            throw new InvalidOperationException("Custom models are not initialized.");

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

        public void SetPlayerModel(IPlayer player, string modelName)
        {
            if (_modelInfos is null)
            {
                throw new InvalidOperationException("Custom models are not initialized.");
            }

            if (player is not { IsValid: true } || player.PlayerPawn is not { IsValid: true })
            {
                _logger.LogError("Player is not valid.");
                return;
            }

            if (!_modelInfos.TryGetValue(modelName, out var info))
            {
                _logger.LogError("Cannot find the modle, modle name: {modleName}", modelName);
                return;
            }

            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (player is not { IsValid: true } || player.PlayerPawn is not { IsValid: true })
                    return;
                player.PlayerPawn.SetModel(info.ModelPath);
            });

            if (info.MeshGroupIndex.HasValue)
            {
                _core.Scheduler.NextWorldUpdate(() =>
                {
                    if (player is not { IsValid: true } || player.PlayerPawn is not { IsValid: true })
                        return;

                    var bodyComponent = player.PlayerPawn.CBodyComponent;

                    if (bodyComponent is null)
                        return;

                    var sceneNode = bodyComponent.SceneNode;

                    if (sceneNode is null)
                        return;

                    sceneNode.GetSkeletonInstance().ModelState.MeshGroupMask =
                        ComputeMeshGroupMask([info.MeshGroupIndex.Value], []);
                });
            }
        }

        private static ulong ComputeMeshGroupMask(int[] enabledMeshGroups, Dictionary<int, int> fixedMeshGroups)
        {
            ulong meshGroupMask = 0;

            foreach (var meshGroup in enabledMeshGroups)
            {
                meshGroupMask |= (ulong)1 << meshGroup;
            }

            foreach (var fixedMeshGroup in fixedMeshGroups)
            {
                if (fixedMeshGroup.Value == 0)
                {
                    meshGroupMask &= ~((ulong)1 << fixedMeshGroup.Key);
                }
                else
                {
                    meshGroupMask |= (ulong)1 << fixedMeshGroup.Key;
                }
            }

            return meshGroupMask;
        }
    }
}
