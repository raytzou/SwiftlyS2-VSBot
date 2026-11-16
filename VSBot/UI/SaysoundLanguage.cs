using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;

namespace VSBot.UI
{
    public class SaysoundLanguage : ISaysoundLanguage
    {
        private readonly ISwiftlyCore _core;
        private readonly ILogger<SaysoundLanguage> _logger;
        private readonly IPlayerDataService _playerDataService;

        public SaysoundLanguage(ISwiftlyCore core, ILogger<SaysoundLanguage> logger, IPlayerDataService playerDataService)
        {
            _core = core;
            _logger = logger;
            _playerDataService = playerDataService;
        }

        private CCSCustomHudLayout? _saysoundLanguageUI;

        public void Initialize()
        {
            var maxPlayers = _core.PlayerManager.MaxPlayers;

            _saysoundLanguageUI = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
            _saysoundLanguageUI.StrLayout = "panorama/layout/custom_game/ss_lang.xml";
            _saysoundLanguageUI.StrLayoutUpdated();
            _saysoundLanguageUI.DispatchSpawn();
            _core.Event.OnCustomHudClicked += OnClicked;
        }

        public void Open(int playerId)
        {
            var maxPlayers = _core.PlayerManager.MaxPlayers;

            if (playerId < 0 || playerId >= maxPlayers)
                return;

            if (_saysoundLanguageUI is null)
            {
                _logger.LogError("SaysoundLanguage is null.");
                throw new InvalidOperationException("SaysoundLanguage is null. Is ModuleUI initialized?");
            }

            var player = _core.PlayerManager.GetPlayer(playerId);

            if (player is null || !player.IsValid)
                return;

            var localizer = _core.Translation.GetPlayerLocalizer(player);
            var title = localizer["ui.ss_lang.title"];
            var optionEN = localizer["ui.ss_lang.option.en"];
            var optionJP = localizer["ui.ss_lang.option.jp"];
            var optionTCH = localizer["ui.ss_lang.option.tch"];

            _saysoundLanguageUI.SetDialogVariableStringForPlayer(
                playerId,
                "ss_lang_title",
                "title",
                title
            );

            _saysoundLanguageUI.SetDialogVariableStringForPlayer(
                playerId,
                "ss_lang_label_en",
                "label",
                optionEN
            );

            _saysoundLanguageUI.SetDialogVariableStringForPlayer(
                playerId,
                "ss_lang_label_ja",
                "label",
                optionJP
            );

            _saysoundLanguageUI.SetDialogVariableStringForPlayer(
                playerId,
                "ss_lang_label_zh_tw",
                "label",
                optionTCH
            );

            _saysoundLanguageUI.SetHasClassForPlayer(
                playerId,
                "ss_lang_root",
                "is-open",
                EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
            );

            _saysoundLanguageUI.SetInputCaptureEnabledForPlayer(playerId, true);
        }

        public void OnClicked(IOnCustomHudClickedEvent @event)
        {
            if (_saysoundLanguageUI is null)
            {
                _logger.LogError("SaysoundLanguage is null.");
                throw new InvalidOperationException("SaysoundLanguage is null. Is ModuleUI initialized?");
            }

            if (@event.CustomHudLayout.Address != _saysoundLanguageUI.Address) // TODO: UIModule UIEventDispatcher
                return;

            var player = _core.PlayerManager.GetPlayer(@event.PlayerId);

            if (player == null || !player.IsValid)
                return;

            var playerCache = _playerDataService.GetPlayerCache(player.SteamID);

            if (playerCache is null)
            {
                _logger.LogError("Player Cache is null! Steam ID: {steamId}", player.SteamID);
                Close(player.PlayerID);
                return;
            }

            switch (@event.ButtonId)
            {
                case "ss_lang_close":
                    Close(player.PlayerID);
                    return;
                case "ss_lang_en":
                    playerCache.Language = Language.English;
                    break;
                case "ss_lang_ja":
                    playerCache.Language = Language.Japanese;
                    break;
                case "ss_lang_zh_tw":
                    playerCache.Language = Language.ChineseTW;
                    break;
            }

            Close(player.PlayerID);
            _playerDataService.UpdatePlayerCache(playerCache);
            player.SendChat("Your saysound language has been updated.");
        }

        public void Close(int playerId)
        {
            if (_saysoundLanguageUI is null)
            {
                _logger.LogError("SaysoundLanguage is null.");
                throw new InvalidOperationException("SaysoundLanguage is null. Is ModuleUI initialized?");
            }

            _saysoundLanguageUI.SetInputCaptureEnabledForPlayer(playerId, false);

            _saysoundLanguageUI.SetHasClassForPlayer(
                playerId,
                "ss_lang_root",
                "is-open",
                EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass
            );
        }
    }
}
