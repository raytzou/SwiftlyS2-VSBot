using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using VSBot.Services.Interfaces;
using VSBot.UI.Interfaces;
using VSBot.UI.ViewState;

namespace VSBot.UI
{
    public class ModelSelect : IModelSelect, IUIComponentLifecycle, IClickableUIComponent
    {
        private const int PageSize = 8;
        private const string SlotPrefix = "models_slot_";

        private readonly ISwiftlyCore _core;
        private readonly ILogger<ModelSelect> _logger;
        private readonly IModelService _modelService;
        private readonly IPlayerDataService _playerDataService;

        public ModelSelect(ISwiftlyCore core, ILogger<ModelSelect> logger, IModelService modelService, IPlayerDataService playerDataService)
        {
            _core = core;
            _logger = logger;
            _modelService = modelService;
            _playerDataService = playerDataService;
        }

        private CCSCustomHudLayout? _modelSelectUI;

        // Only valid while a player has the UI open; SessionId guards against slot reuse.
        private readonly Dictionary<int, ModelSelectViewState> _states = new();

        // Skin classes live on the layout per player, so they outlive Close; index 0 is the preview, 1..8 are the cards.
        private readonly Dictionary<int, string?[]> _skinClasses = new();

        CCSCustomHudLayout? IUIComponentLifecycle.Layout => _modelSelectUI;

        void IUIComponentLifecycle.OnPlayerDisconnect(int playerId)
        {
            _states.Remove(playerId);
            _skinClasses.TryGetValue(playerId, out var skins);
            _skinClasses.Remove(playerId);

            var layout = _modelSelectUI;
            if (layout is null || !layout.IsValid) return;

            Close(playerId);

            if (skins is not null)
            {
                for (var index = 0; index < skins.Length; index++)
                {
                    if (skins[index] is not null)
                        SetClass(layout, playerId, SkinPanelId(index), skins[index]!, false);
                }
            }

            layout.RemoveDialogVariableStringForPlayer(playerId, "models_count", "count");
            layout.RemoveDialogVariableStringForPlayer(playerId, "models_page", "page");
            layout.RemoveDialogVariableStringForPlayer(playerId, "models_preview_name", "name");

            SetClass(layout, playerId, "models_previous", "disabled", false);
            SetClass(layout, playerId, "models_next", "disabled", false);
            SetClass(layout, playerId, "models_apply", "disabled", false);

            for (var slot = 1; slot <= PageSize; slot++)
            {
                layout.RemoveDialogVariableStringForPlayer(playerId, $"models_index_{slot}", "index");
                layout.RemoveDialogVariableStringForPlayer(playerId, $"models_name_{slot}", "name");

                SetClass(layout, playerId, $"{SlotPrefix}{slot}", "empty", false);
                SetClass(layout, playerId, $"{SlotPrefix}{slot}", "selected", false);
            }
        }

        void IUIComponentLifecycle.Initialize()
        {
            _states.Clear();
            _skinClasses.Clear();

            _modelSelectUI = _core.EntitySystem.CreateEntity<CCSCustomHudLayout>();
            _modelSelectUI.StrLayout = "panorama/layout/custom_game/model_select.xml";
            _modelSelectUI.StrLayoutUpdated();
            _modelSelectUI.DispatchSpawn();
        }

        void IUIComponentLifecycle.Unload()
        {
            var layout = _modelSelectUI;
            _modelSelectUI = null;
            _states.Clear();
            _skinClasses.Clear();

            if (layout is null || !layout.IsValid) return;

            try
            {
                // Per-player capture overrides the global HUD state.
                for (var playerId = 0; playerId < _core.PlayerManager.MaxPlayers; playerId++)
                {
                    if (layout.IsInputCaptureEnabledForPlayer(playerId))
                        layout.SetInputCaptureEnabledForPlayer(playerId, false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to release HUD input during unload.");
            }
            finally
            {
                if (layout.IsValid) layout.Despawn();
            }
        }

        public void Open(int playerId)
        {
            if (_modelSelectUI is null || !_modelSelectUI.IsValid) return;

            if (playerId < 0 || playerId >= _core.PlayerManager.MaxPlayers) return;

            var player = _core.PlayerManager.GetPlayer(playerId);

            if (player is null || player.IsFakeClient) return;

            var models = _modelService.ModelNames
                .Select(name => (name, _modelService.GetImageKey(name)))
                .ToArray();

            var state = new ModelSelectViewState
            {
                SessionId = player.SessionId,
                Models = models
            };
            _states[playerId] = state;

            Render(playerId, state);

            _modelSelectUI.SetHasClassForPlayer(
                playerId,
                "models_root",
                "hidden",
                EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass
            );

            _modelSelectUI.SetInputCaptureEnabledForPlayer(playerId, true);
        }

        public void Close(int playerId)
        {
            _states.Remove(playerId);

            if (_modelSelectUI is null || !_modelSelectUI.IsValid) return;

            _modelSelectUI.SetHasClassForPlayer(
                playerId,
                "models_root",
                "hidden",
                EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
            );

            _modelSelectUI.SetInputCaptureEnabledForPlayer(playerId, false);
        }

        void IClickableUIComponent.OnClicked(IOnCustomHudClickedEvent @event)
        {
            if (_modelSelectUI is null || !_modelSelectUI.IsValid) return;

            var player = _core.PlayerManager.GetPlayer(@event.PlayerId);

            if (player is null || player.IsFakeClient) return;

            if (!_states.TryGetValue(player.PlayerID, out var state) || state.SessionId != player.SessionId)
            {
                // Stale capture from a previous occupant of this slot.
                Close(player.PlayerID);
                return;
            }

            var buttonId = @event.ButtonId;

            switch (buttonId)
            {
                case "models_close":
                    Close(player.PlayerID);
                    return;
                case "models_reset":
                    var playerCache = _playerDataService.GetPlayerCache(player.SteamID);

                    if (playerCache is null)
                    {
                        if (_playerDataService.GetPlayerCacheState(player.SteamID) == PlayerCacheState.Loading)
                        {
                            player.SendChat("Your data is still loading, please try again later.");
                            return;
                        }

                        player.SendChat("Cannot set the model, please reconnect to server!");
                        _logger.LogError("Player cache is null, SteamID: {steamId}", player.SteamID);
                        return;
                    }

                    playerCache.SelectedModelName = null;
                    _playerDataService.UpdatePlayerCache(playerCache);
                    var defaultModelPath = _modelService.GetDefaultModelPath(player.SteamID);
                    if (!player.IsAlive || string.IsNullOrEmpty(defaultModelPath))
                    {
                        player.SendChat("Your model will be reset on your next spawn.");
                        return;
                    }

                    _modelService.SetPlayerModelByPath(player, defaultModelPath, (currentPlayer, applied) =>
                        currentPlayer.SendChat(applied
                            ? "Your model has been reset to default."
                            : "Your model will be reset on your next spawn."));

                    return;
                case "models_previous":
                    if (state.Page > 0) state.Page--;
                    break;
                case "models_next":
                    if (state.Page < GetPageCount(state) - 1) state.Page++;
                    break;
                case "models_apply":
                    Apply(player, state);
                    return;
                default:
                    if (!TrySelectSlot(buttonId, state))
                        return;
                    break;
            }

            Render(player.PlayerID, state);
        }

        private static bool TrySelectSlot(string buttonId, ModelSelectViewState state)
        {
            if (!buttonId.StartsWith(SlotPrefix, StringComparison.Ordinal)
                || !int.TryParse(buttonId.AsSpan(SlotPrefix.Length), out var slot)
                || slot < 1 || slot > PageSize)
                return false;

            var index = state.Page * PageSize + slot - 1;

            if (index >= state.Models.Length) return false;

            state.SelectedName = state.Models[index].Name;
            return true;
        }

        private void Apply(IPlayer player, ModelSelectViewState state)
        {
            if (state.SelectedName is null) return;

            var playerCache = _playerDataService.GetPlayerCache(player.SteamID);

            if (playerCache is null)
            {
                if (_playerDataService.GetPlayerCacheState(player.SteamID) == PlayerCacheState.Loading)
                {
                    player.SendChat("Your data is still loading, please try again later.");
                    return;
                }

                player.SendChat("Cannot set the model, please reconnect to server!");
                _logger.LogError("Player cache is null, SteamID: {steamId}", player.SteamID);
                return;
            }

            playerCache.SelectedModelName = state.SelectedName;
            _playerDataService.UpdatePlayerCache(playerCache);
            if (player.IsAlive)
                _modelService.SetPlayerModel(player, state.SelectedName);
            player.SendChat($"Your selected model is now {state.SelectedName}.");
        }

        private static int GetPageCount(ModelSelectViewState state)
            => Math.Max(1, (state.Models.Length + PageSize - 1) / PageSize);

        private void Render(int playerId, ModelSelectViewState state)
        {
            var layout = _modelSelectUI;
            if (layout is null || !layout.IsValid) return;

            var pageCount = GetPageCount(state);
            state.Page = Math.Clamp(state.Page, 0, pageCount - 1);

            layout.SetDialogVariableStringForPlayer(playerId, "models_count", "count", $"{state.Models.Length} MODELS");
            layout.SetDialogVariableStringForPlayer(playerId, "models_page", "page", $"{state.Page + 1} / {pageCount}");

            SetClass(layout, playerId, "models_previous", "disabled", state.Page <= 0);
            SetClass(layout, playerId, "models_next", "disabled", state.Page >= pageCount - 1);

            for (var slot = 1; slot <= PageSize; slot++)
            {
                var index = state.Page * PageSize + slot - 1;
                var exists = index < state.Models.Length;

                layout.SetDialogVariableStringForPlayer(playerId, $"models_index_{slot}", "index", exists ? $"#{index + 1}" : string.Empty);
                layout.SetDialogVariableStringForPlayer(playerId, $"models_name_{slot}", "name", exists ? state.Models[index].Name : string.Empty);

                SetClass(layout, playerId, $"{SlotPrefix}{slot}", "empty", !exists);
                SetClass(layout, playerId, $"{SlotPrefix}{slot}", "selected", exists && state.Models[index].Name == state.SelectedName);
                SetSkin(layout, playerId, slot, exists ? state.Models[index].ImageKey : null);
            }

            var selected = state.SelectedName;
            var selectedKey = selected is null
                ? null
                : state.Models.FirstOrDefault(m => m.Name == selected).ImageKey;

            layout.SetDialogVariableStringForPlayer(playerId, "models_preview_name", "name", selected ?? "-");
            SetSkin(layout, playerId, 0, selectedKey);
            SetClass(layout, playerId, "models_apply", "disabled", selected is null);
        }

        private static string SkinPanelId(int index)
            => index == 0 ? "models_preview_image" : $"models_image_{index}";

        private void SetSkin(CCSCustomHudLayout layout, int playerId, int index, string? imageKey)
        {
            var panelId = SkinPanelId(index);

            if (!_skinClasses.TryGetValue(playerId, out var skins))
                _skinClasses[playerId] = skins = new string?[PageSize + 1];

            var skinClass = string.IsNullOrEmpty(imageKey) ? null : $"skin_{imageKey}";

            if (skins[index] == skinClass) return;

            if (skins[index] is not null)
                SetClass(layout, playerId, panelId, skins[index]!, false);

            if (skinClass is not null)
                SetClass(layout, playerId, panelId, skinClass, true);

            skins[index] = skinClass;
        }

        private static void SetClass(CCSCustomHudLayout layout, int playerId, string panelId, string className, bool enabled)
        {
            layout.SetHasClassForPlayer(
                playerId,
                panelId,
                className,
                enabled
                    ? EHudPanelClassStatus_t.k_eHudPanelClassStatus_HasClass
                    : EHudPanelClassStatus_t.k_eHudPanelClassStatus_DoesNotHaveClass
            );
        }
    }
}
