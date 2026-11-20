namespace VSBot.Services.Interfaces
{
    public interface IMusicService
    {
        string CurrentRoundMusicName { get; }

        void Initialize();
        void PlayAfterJoinTeam(int playerId);
        void PlayOnRound();
        void PlayOnGameOver();
        void PlayOnRoundWin();
        void PlayOnRoundLoose();
        void StopCurrentMusic();
        void Reset();
        void RemovePlayer(int playerId);
    }
}
