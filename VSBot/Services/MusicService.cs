using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.ProtobufDefinitions;
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

        public string CurrentRoundMusicName => _roundMusic?.DisplayName ?? string.Empty;

        private Music? _musicList;
        private SoundEventInfo? _roundMusic;
        private uint _currentMusic;
        private CancellationTokenSource? _roundPlaybackTimer;
        private readonly Dictionary<int, CancellationTokenSource> _joinPlaybackTimers = new();
        private List<(uint emitId, int playerId)> _currentMusics = new();

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

            CancelJoinPlayback(playerId);
            if (_gameStateService.IsRoundEnd) return;

            var currentRound = _gameStateService.CurrentRound;
            var maxRounds = _config.CurrentValue.MaxRounds;

            if (currentRound == 0) // warmup
            {
                var warmupMusic = _musicList.Warmup;
                var random = Random.Shared.Next(warmupMusic.Count);

                ScheduleJoinPlayback(warmupMusic[random], playerId);
            }
            else if (currentRound < maxRounds)
            {
                if (_roundPlaybackTimer is { IsCancellationRequested: false }) return;

                var targetMusic = _roundMusic;
                if (targetMusic is null) return;

                ScheduleJoinPlayback(targetMusic.EventName, playerId);
            }
            else // game over
            {
                var endGame = _musicList.EndGame;
                var random = Random.Shared.Next(endGame.Count);

                ScheduleJoinPlayback(endGame[random], playerId);
            }
        }

        private void ScheduleJoinPlayback(string eventName, int playerId)
        {
            var player = _core.PlayerManager.GetPlayer(playerId);
            if (player is null || !player.IsValid || player.IsFakeClient) return;
            var steamId = player.SteamID;
            var timer = _core.Scheduler.DelayBySeconds(2f, () =>
            {
                _joinPlaybackTimers.Remove(playerId);
                var currentPlayer = _core.PlayerManager.GetPlayer(playerId);
                if (currentPlayer is null || !currentPlayer.IsValid || currentPlayer.IsFakeClient
                    || currentPlayer.SteamID != steamId || _gameStateService.IsRoundEnd) return;
                PlaySound(eventName, playerId);
            });
            _joinPlaybackTimers[playerId] = timer;
            _core.Scheduler.StopOnMapChange(timer);
        }

        private void CancelJoinPlayback(int playerId)
        {
            if (_joinPlaybackTimers.Remove(playerId, out var timer)) timer.Cancel();
        }

        public void RemovePlayer(int playerId)
        {
            CancelJoinPlayback(playerId);
            _currentMusics.RemoveAll(music => music.playerId == playerId);
        }

        private void CancelPendingPlayback()
        {
            _roundPlaybackTimer?.Cancel();
            _roundPlaybackTimer = null;
            foreach (var timer in _joinPlaybackTimers.Values) timer.Cancel();
            _joinPlaybackTimers.Clear();
            _roundMusic = null;
        }

        public void PlayOnRound()
        {
            if (_musicList is null)
            {
                throw new InvalidOperationException("Music is not initialized or failed to deserialize");
            }

            StopCurrentMusic();

            var roundMusic = _musicList.Round;
            var random = Random.Shared.Next(roundMusic.Count);

            _roundMusic = roundMusic[random];

            var freezeTime = _core.ConVar.Find<int>("mp_freezetime")!.Value;

            _roundPlaybackTimer = _core.Scheduler.DelayBySeconds(freezeTime, () =>
            {
                _roundPlaybackTimer = null;
                PlaySoundToAll(roundMusic[random].EventName);
            });

            _core.Scheduler.StopOnMapChange(_roundPlaybackTimer);
        }

        public void PlayOnRoundWin()
        {
            if (_musicList is null)
            {
                throw new InvalidOperationException("Music is not initialized or failed to deserialize");
            }

            var roundWinMusic = _musicList.Win;
            var random = Random.Shared.Next(roundWinMusic.Count);

            PlaySoundToAll(roundWinMusic[random]);
        }

        public void PlayOnRoundLoose()
        {
            if (_musicList is null)
            {
                throw new InvalidOperationException("Music is not initialized or failed to deserialize");
            }

            var roundLooseMusic = _musicList.Loose;
            var random = Random.Shared.Next(roundLooseMusic.Count);

            PlaySoundToAll(roundLooseMusic[random]);
        }

        public void PlayOnGameOver()
        {
            if (_musicList is null)
            {
                throw new InvalidOperationException("Music is not initialized or failed to deserialize");
            }

            StopCurrentMusic();

            var endGameMusic = _musicList.EndGame;
            var random = Random.Shared.Next(endGameMusic.Count);

            PlaySoundToAll(endGameMusic[random]);
        }

        public void StopCurrentMusic()
        {
            CancelPendingPlayback();
            SendStopSoundMessageToAll(_currentMusic);

            foreach (var music in _currentMusics)
            {
                SendStopSoundMessage(music.emitId, music.playerId);
            }

            _currentMusic = default;
            _currentMusics.Clear();
        }

        public void Reset()
        {
            CancelPendingPlayback();
            _currentMusic = default;
            _currentMusics.Clear();
        }

        private void PlaySound(string eventName, int playerId)
        {
            const float volume = 5f;

            using var soundEvent = new SoundEvent()
            {
                Name = eventName,
                Volume = volume,
            };

            soundEvent.Recipients.AddRecipient(playerId);
            _currentMusics.Add((soundEvent.Emit(), playerId));
        }

        private void PlaySoundToAll(string eventName)
        {
            const float volume = 5f;

            using var soundEvent = new SoundEvent()
            {
                Name = eventName,
                Volume = volume,
            };

            soundEvent.Recipients.AddAllPlayers();
            _currentMusic = soundEvent.Emit();
        }

        private void SendStopSoundMessage(uint emitId, int playerId)
        {
            _core.NetMessage.Send<CMsgSosStopSoundEvent>(msg =>
            {
                msg.SoundeventGuid = unchecked((int)emitId);
                msg.Recipients.AddRecipient(playerId);
            });
        }

        private void SendStopSoundMessageToAll(uint emitId)
        {
            _core.NetMessage.Send<CMsgSosStopSoundEvent>(msg =>
            {
                msg.SoundeventGuid = unchecked((int)emitId);
                msg.Recipients.AddAllPlayers();
            });
        }
    }
}
