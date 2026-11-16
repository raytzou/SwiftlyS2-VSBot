using VSBot.UI.Interfaces;

namespace VSBot.Modules
{
    public class UIModule
    {
        private readonly IMotd _motd;
        private readonly IScoreboard _scoreboard;
        private readonly ISaysoundLanguage _saysoundLanguage;

        public UIModule(
            IMotd motd,
            IScoreboard scoreboard,
            ISaysoundLanguage saysoundLanguage
            )
        {
            _motd = motd;
            _scoreboard = scoreboard;
            _saysoundLanguage = saysoundLanguage;
        }

        public void Initialize()
        {
            _motd.Initialize();
            _scoreboard.Initialize();
            _saysoundLanguage.Initialize();
        }
    }
}
