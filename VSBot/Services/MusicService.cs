using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Sounds;
using System.Text.Json;
using VSBot.Models;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class MusicService : IMusicService
    {
        private readonly ISwiftlyCore _core;
        private readonly IOptionsMonitor<Config> _config;
        private readonly ILogger<MusicService> _logger;
        private readonly IGameStateService _gameStateService;

        public MusicService(ISwiftlyCore core, IOptionsMonitor<Config> config, ILogger<MusicService> logger, IGameStateService gameStateService)
        {
            _core = core;
            _config = config;
            _logger = logger;
            _gameStateService = gameStateService;
        }

        public string CurrentRoundMusicName => _roundMusicName;

        private Music? _musicList;
        private string _roundMusicName = string.Empty;

        public void Initialize()
        {
            _musicList = new();

            var gameRoot = _core.GameDirectory;
            var soundInfoFileName = _config.CurrentValue.SoundInfoFileName;

            if (string.IsNullOrEmpty(soundInfoFileName))
            {
                throw new InvalidOperationException("Sound Info File Name is not set");
            }

            var soundInfoFilePath = Path.Join(gameRoot, soundInfoFileName);
            if (!File.Exists(soundInfoFilePath))
            {
                throw new InvalidOperationException($"{soundInfoFileName} could not be found in game root folder");
            }

            var json = File.ReadAllText(soundInfoFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            _musicList = JsonSerializer.Deserialize<Music>(json, options);

            if (_musicList is null)
            {
                _logger.LogError("{soundInfoFileName} deserialization failed", soundInfoFileName);
                return;
            }

            _logger.LogInformation("Music initialized, Warmup: {warmupCount}, Round: {roundCount}, EndGame: {endGameCount}, Loose: {looseCount}, Win: {winCount}", _musicList.Warmup.Count, _musicList.Round.Count, _musicList.EndGame.Count, _musicList.Loose.Count, _musicList.Win.Count);
        }

        public void PlayAfterJoinTeam(int playerId)
        {
            if (_musicList is null)
            {
                throw new InvalidOperationException("Music is not initialized or failed to deserialize");
            }

            var currentRound = _gameStateService.CurrentRound;
            var maxRounds = _config.CurrentValue.MaxRounds;
            const float delayPlay = 1f;

            if (currentRound == 0) // warmup
            {
                var warmupMusic = _musicList.Warmup;
                var random = Random.Shared.Next(warmupMusic.Count);

                var timer = _core.Scheduler.DelayBySeconds(delayPlay, () =>
                {
                    PlaySound(warmupMusic[random], playerId);
                });

                _core.Scheduler.StopOnMapChange(timer);
            }
            else if (currentRound < maxRounds)
            {
                var roundMusic = _musicList.Round;
                var targetMusic = new SoundEventInfo();

                foreach (var music in roundMusic)
                {
                    if (music.DisplayName == _roundMusicName)
                    {
                        targetMusic = music;
                        break;
                    }
                }

                var timer = _core.Scheduler.DelayBySeconds(delayPlay, () =>
                {
                    PlaySound(targetMusic.EventName, playerId);
                });

                _core.Scheduler.StopOnMapChange(timer);
            }
            else // game over
            {
                var endGame = _musicList.EndGame;
                var random = Random.Shared.Next(endGame.Count);

                var timer = _core.Scheduler.DelayBySeconds(delayPlay, () =>
                {
                    PlaySound(endGame[random], playerId);
                });

                _core.Scheduler.StopOnMapChange(timer);
            }
        }

        public void PlayOnRound()
        {
            throw new NotImplementedException();
        }

        public void PlayOnRoundWin()
        {
            throw new NotImplementedException();
        }

        public void PlayOnRoundLoose()
        {
            throw new NotImplementedException();
        }

        public void PlayOnGameOver()
        {
            throw new NotImplementedException();
        }

        private void PlaySound(string eventName, int playerId)
        {
            const float volume = .5f;

            using var soundEvent = new SoundEvent()
            {
                Name = eventName,
                Volume = volume,
            };

            soundEvent.Recipients.AddRecipient(playerId);
            soundEvent.Emit();
        }

        private void PlaySoundToAll(string eventName)
        {
            const float volume = .5f;

            using var soundEvent = new SoundEvent()
            {
                Name = eventName,
                Volume = volume,
            };

            soundEvent.Recipients.AddAllPlayers();
            soundEvent.Emit();
        }
    }
}
