namespace VSBot.Services.Interfaces
{
    public interface ISaysoundService
    {
        public Dictionary<string, (string SoundEvent, string Content, string TWContent, string JPContent)> Saysound { get; }

        public void Initialize();
        public void PlaySaysound(string soundEventName, float pitch);
    }
}
