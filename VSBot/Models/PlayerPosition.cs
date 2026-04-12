using SwiftlyS2.Shared.Natives;

namespace VSBot.Models
{
    public class PlayerPosition
    {
        public Vector Origin { get; set; }
        public QAngle Rotation { get; set; }
        public Vector Velocity { get; set; }
    }
}
