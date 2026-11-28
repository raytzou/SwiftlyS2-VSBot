using CsvHelper;
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
        private bool _unloaded;
        private readonly Dictionary<ulong, string> _defaultModels = new();

        public string? GetImageKey(string modelName)
        {
            if (_modelInfos is null)
            {
                throw new InvalidOperationException("Custom models are not initialized.");
            }

            return _modelInfos.TryGetValue(modelName, out var info) && !string.IsNullOrEmpty(info.ImageKey)
                ? Path.GetFileNameWithoutExtension(info.ImageKey)
                : null;
        }

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

            using var csv = CsvReader.FromFile(modelInfoFile);

            while (csv.Read())
            {
                var modelName = csv.GetString("ModelName");

                if (string.IsNullOrEmpty(modelName))
                    continue;

                var modelPath = csv.GetString("ModelPath");
                var armPath = csv.GetString("ArmPath");
                var meshGroupIndex = csv.GetString("MeshGroupIndex");
                var imageKey = csv.GetString("ImageKey");

                _modelInfos.Add(modelName, new()
                {
                    ModelPath = modelPath,
                    ArmPath = armPath,
                    MeshGroupIndex = int.TryParse(meshGroupIndex, out var index) ? index : null,
                    ImageKey = imageKey
                });
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
            if (_unloaded) return;

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
                _logger.LogError("Cannot find the model, model name: {modelName}", modelName);
                return;
            }

            var sessionId = player.SessionId;
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_unloaded) return;
                var currentPlayer = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
                if (currentPlayer is not { IsValid: true, IsAlive: true }
                    || currentPlayer.PlayerPawn is not { IsValid: true })
                    return;
                currentPlayer.PlayerPawn.SetModel(info.ModelPath);
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

        // Called synchronously from Spawn, before any custom model is applied.
        public void SetPlayerDefaultModelPath(IPlayer player)
        {
            if (_unloaded) return;

            var currentPlayer = _core.PlayerManager.GetPlayerFromSessionId(player.SessionId);
            if (currentPlayer is not { IsValid: true, IsAlive: true }
                || currentPlayer.PlayerPawn is not { IsValid: true } pawn)
                return;

            var steamId = currentPlayer.SteamID;
            if (_defaultModels.ContainsKey(steamId)) return;

            var sceneNode = pawn.CBodyComponent?.SceneNode;
            if (sceneNode is null) return;

            var defaultModelPath = sceneNode.GetSkeletonInstance().ModelState.ModelName;
            if (string.IsNullOrWhiteSpace(defaultModelPath)) return;

            // Custom assets can also live under agents/, so compare actual catalog paths.
            var normalizedPath = defaultModelPath.Replace('\\', '/');
            if (_modelInfos is not null && _modelInfos.Values.Any(info =>
                string.Equals(info.ModelPath.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase)))
                return;

            _defaultModels.Add(steamId, defaultModelPath);
        }

        public void SetPlayerModelByPath(IPlayer player, string modelPath)
        {
            if (_unloaded) return;

            if (player is not { IsValid: true } || player.PlayerPawn is not { IsValid: true })
            {
                _logger.LogError("Player is not valid.");
                return;
            }

            var sessionId = player.SessionId;
            _core.Scheduler.NextWorldUpdate(() =>
            {
                if (_unloaded) return;
                var currentPlayer = _core.PlayerManager.GetPlayerFromSessionId(sessionId);
                if (currentPlayer is not { IsValid: true, IsAlive: true }
                    || currentPlayer.PlayerPawn is not { IsValid: true })
                    return;
                currentPlayer.PlayerPawn.SetModel(modelPath);
            });
        }

        public void Unload()
        {
            _unloaded = true;
            ClearDefaultModelPaths();
        }

        public string? GetDefaultModelPath(ulong steamId) =>
            _defaultModels.TryGetValue(steamId, out var defaultModelPath) ? defaultModelPath : null;

        public void RemoveDefaultModelPath(ulong steamId) =>
            _defaultModels.Remove(steamId);

        public void ClearDefaultModelPaths() =>
            _defaultModels.Clear();
    }
}
