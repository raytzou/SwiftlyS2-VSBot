namespace VSBot.Models
{
    public class Config
    {
        public bool IsDebug { get; set; } = false;
        public string ConnectionString { get; set; } = string.Empty;
        public int ReviveCostScore { get; set; } = 25;
        public int BotQuota { get; set; } = 23;
        public int SpawnPointCount { get; set; } = 25;
        public int MaxRounds { get; set; } = 9;
        public string SaysoundSoundEventFile { get; set; } = string.Empty;
        public string SaysoundCSVFIleName { get; set; } = string.Empty;
        public string SoundInfoFileName { get; set; } = string.Empty;
        public string SoundEventFile { get; set; } = string.Empty;
        public string ModelInfoFile { get; set; } = string.Empty;
    }
}
