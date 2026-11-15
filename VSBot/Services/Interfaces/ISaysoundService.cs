namespace VSBot.Services.Interfaces
{
    public interface ISaysoundService
    {
        public Dictionary<string, (string SoundEvent, string Content, string TWContent, string JPContent)> Saysound { get; }
        public IReadOnlyDictionary<string, float> PitchMapper { get; }

        public void Initialize();
        public void PlaySaysound(string soundEventName, float pitch);
        public void BroadcastSaysound(int playerId, string saysoundKeyword, string pitchText);
    }
}
