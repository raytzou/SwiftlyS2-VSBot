using CsvHelper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Sounds;
using SwiftlyS2.Shared.Translation;
using VSBot.Classes;
using VSBot.Models;
using VSBot.Services.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot.Services
{
    public class SaysoundService : ISaysoundService
    {
        private readonly IOptionsMonitor<Config> _config;
        private readonly ILogger<SaysoundService> _logger;
        private readonly ISwiftlyCore _core;
        private readonly IGameStateService _gameStateService;
        private readonly IPlayerDataService _playerDataService;
        private const byte DefaultSaysoundVolume = 50;

        public SaysoundService(IOptionsMonitor<Config> config, ILogger<SaysoundService> logger, ISwiftlyCore core, IGameStateService gameStateService, IPlayerDataService playerDataService)
        {
            _config = config;
            _logger = logger;
            _core = core;
            _gameStateService = gameStateService;
            _playerDataService = playerDataService;
        }

        public Dictionary<string, (string SoundEvent, string Content, string TWContent, string JPContent)> Saysound => _saysound
            ?? throw new InvalidOperationException("Saysound is not initialized");

        private Dictionary<string, (string SoundEvent, string Content, string TWContent, string JPContent)>? _saysound;

        public IReadOnlyDictionary<string, float> PitchMapper => _pitchMapper;

        private static readonly IReadOnlyDictionary<string, float> _pitchMapper = new Dictionary<string, float>()
        {
            { "qq", 1.5f },
            { "q", 1.3f },
            { "f", 1.15f },
            { "s", 0.85f },
            { "d", 0.75f }
        };

        public void Initialize()
        {
            var saysoundCSVFileName = _config.CurrentValue.SaysoundCSVFIleName;
            var pluginPath = _core.PluginPath;
            var fullPath = Path.Combine(pluginPath, saysoundCSVFileName);

            if (!File.Exists(fullPath))
            {
                _logger.LogError("Saysound CSV File name is not set.");
                throw new InvalidOperationException("Saysound CSV File name is not set.");
            }

            _saysound = new();

            using var csv = CsvReader.FromFile(fullPath);

            while (csv.Read())
            {
                var saysoundName = csv.GetString("SaysoundName");

                if (string.IsNullOrEmpty(saysoundName))
                    continue;

                var japaneseContent = csv.GetString("JapaneseContent");
                var tChineseContent = csv.GetString("TChineseContent");
                var englishContent = csv.GetString("EnglishContent");
                var soundEvent = csv.GetString("SoundEvent");

                _saysound.Add(saysoundName, new(
                    soundEvent,
                    englishContent,
                    tChineseContent,
                    japaneseContent
                ));
            }

            _logger.LogInformation("Saysound Initialized, count: {count}", _saysound.Count);
        }

        public void PlaySaysound(string soundEventName, float pitch)
        {
            var players = _gameStateService.Players;

            foreach (var playerPair in players)
            {
                var player = playerPair.Value;

                if (player is null || !player.IsValid)
                    continue;

                var playerCache = _playerDataService.GetPlayerCache(player.SteamID);
                var volume = (playerCache?.SaysoundVolume ?? DefaultSaysoundVolume) / 100f;

                var sound = new SoundEvent()
                {
                    Name = soundEventName,
                    Volume = volume,
                    Pitch = pitch,
                };

                sound.Recipients.AddRecipient(player.PlayerID);
                sound.Emit();
            }
        }

        public void BroadcastSaysound(int playerId, string saysoundKeyword, string pitchText)
        {
            if (_saysound is null)
            {
                _logger.LogError("Saysound is not initialized");
                throw new NullReferenceException("Saysound is not initialized");
            }

            var sender = _core.PlayerManager.GetPlayer(playerId);
            var players = _gameStateService.Players;
            var saysound = _saysound[saysoundKeyword];

            foreach (var playerPair in players)
            {
                var player = playerPair.Value;

                if (player is null || !player.IsValid)
                    continue;

                var playerLanguage = player.PlayerLanguage;
                var content = saysound.Content;

                if (playerLanguage.Equals(Language.ChineseTW))
                    content = saysound.TWContent;

                if (playerLanguage.Equals(Language.Japanese))
                    content = saysound.JPContent;

                var displayText = _pitchMapper.TryGetValue(pitchText, out var _) || pitchText == "r" ?
                    $"{saysoundKeyword} {pitchText}" : saysoundKeyword;

                player.SendChat($" {Utility.GetTeamColorByPlayer(player)}{sender?.Name}: {ChatColors.Grey}[{displayText}] {ChatColors.Green}{content}");
            }
        }
    }
}
