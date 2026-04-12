namespace VSBot.Models
{
    public class PlayerWeaponStatus
    {
        public bool IsTracking { get; set; }
        public List<string> Weapons { get; set; } = [];
    }
}
