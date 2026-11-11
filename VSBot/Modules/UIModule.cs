using VSBot.UI.Interfaces;

namespace VSBot.Modules
{
    public class UIModule
    {
        private readonly IMotd _motd;
        private readonly IScoreboard _scoreboard;

        public UIModule(
            IMotd motd,
            IScoreboard scoreboard
            )
        {
            _motd = motd;
            _scoreboard = scoreboard;
        }

        public void Initialize()
        {
            _motd.Initialize();
            _scoreboard.Initialize();
        }
    }
}
