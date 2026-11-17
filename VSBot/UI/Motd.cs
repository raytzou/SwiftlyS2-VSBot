using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SchemaDefinitions;
using VSBot.UI.Interfaces;

namespace VSBot.UI
{
    public class Motd : IMotd, IUIComponentLifecycle, IClickableUIComponent
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<Motd> _logger;

        public Motd(ISwiftlyCore core, ILogger<Motd> logger)
        {
            _core = core;
            _logger = logger;
        }

        private CCSCustomHudLayout? _motd;

        CCSCustomHudLayout? IUIComponentLifecycle.Layout => _motd;

        void IUIComponentLifecycle.Unload()
        {
            var layout = _motd;
            _motd = null;

            if (layout is null || !layout.IsValid) return;

            try
            {
                // Per-player capture overrides the global HUD state.
                for (var playerId = 0; playerId < _core.PlayerManager.MaxPlayers; playerId++)
                {
                    try
                    {
                        if (layout.IsInputCaptureEnabledForPlayer(playerId))
                            layout.SetInputCaptureEnabledForPlayer(playerId, false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to release HUD input for player {PlayerId}.", playerId);
                    }
                }
            }
            finally
            {
                if (layout.IsValid) layout.Despawn();
            }
        }

        public void Close(int playerId)
        {
            if (_motd is null || !_motd.IsValid) return;

            _motd.SetHasClassForPlayer(
                playerId,
                "motd_root",
                "hidden",
                EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
            );

            _motd.SetInputCaptureEnabledForPlayer(
                playerId,
                false
            );
        }

        void IUIComponentLifecycle.Initialize()
        {
            _motd = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
            _motd.StrLayout = "panorama/layout/custom_game/cs16_style_motd.xml";
            _motd.StrLayoutUpdated();
            _motd.DispatchSpawn();
        }

        void IClickableUIComponent.OnClicked(IOnCustomHudClickedEvent @event)
        {
            if (_motd is null || !_motd.IsValid) return;

            var player = _core.PlayerManager.GetPlayer(@event.PlayerId);

            if (player == null || !player.IsValid) return;

            switch (@event.ButtonId)
            {
                case "motd_ok":
                    {
                        Close(@event.PlayerId);
                        break;
                    }
            }
        }

        public void Open(int playerId)
        {
            if (_motd is null || !_motd.IsValid) return;

            var player = _core.PlayerManager.GetPlayer(playerId);

            if (player is null || !player.IsValid) return;

            var localizer = _core.Translation.GetPlayerLocalizer(player);
            var bodyMsg = localizer["ui.motd.body", player.Name];
            var link1Msg = localizer["ui.motd.link1"];
            var link2Msg = localizer["ui.motd.link2"];

            _motd.SetDialogVariableStringForPlayer(
                    playerId,
                    "motd_body",
                    "body",
                    bodyMsg
            );

            _motd.SetDialogVariableStringForPlayer(
                playerId,
                "motd_link1",
                "link1",
                link1Msg
            );

            _motd.SetDialogVariableStringForPlayer(
                playerId,
                "motd_link2",
                "link2",
                link2Msg
            );

            _motd.SetHasClassForPlayer(
                playerId,
                "motd_root",
                "hidden",
                EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass
            );

            _motd.SetInputCaptureEnabledForPlayer(
                playerId,
                true
            );
        }
    }
}
