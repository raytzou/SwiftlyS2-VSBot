namespace VSBot.Services.Interfaces
{
    public interface ISaysoundService
    {
        public bool IsInitialized { get; }

        public void Initialize();
        public void PlaySaysound(string saysoundKeyword, string pitchText);
        public void BroadcastSaysound(int playerId, string saysoundKeyword, string pitchText);
        public bool IsPitchText(string pitchText);
        public bool IsSaysound(string saysoundKeyword);
    }
}
