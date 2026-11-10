using VSBot.UI.Interfaces;

namespace VSBot.Modules
{
    public class UIModule
    {
        private readonly IMotd _motd;

        public UIModule(
            IMotd motd
            )
        {
            _motd = motd;
        }

        public void Initialize()
        {
            _motd.Initialize();
        }
    }
}
