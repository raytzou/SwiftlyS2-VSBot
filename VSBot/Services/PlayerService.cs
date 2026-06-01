using SwiftlyS2.Shared;
using VSBot.Models;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class PlayerService : IPlayerService
    {
        private readonly ISwiftlyCore _core;
        private readonly IGameStateService _gameStateService;

        private Dictionary<ulong, PlayerPosition> _positions = [];
        private Dictionary<ulong, PlayerWeaponStatus> _weaponStatuses = [];
        private CancellationTokenSource? _trackingWeaponTimer;

        public PlayerService(ISwiftlyCore core, IGameStateService gameStateService)
        {
            _core = core;
            _gameStateService = gameStateService;
        }

        public void AddPlayerPosition(ulong steamID)
        {
            _positions.Add(steamID, new());
        }

        public void AddPlayerWeaponStatus(ulong steamID)
        {
            _weaponStatuses.Add(steamID, new());
        }

        public void ClearPlayerPositions()
        {
            _positions.Clear();
        }

        public void ClearPlayerWeaponStatuses()
        {
            _weaponStatuses.Clear();
        }

        public void RemovePlayerPosition(ulong steamID)
        {
            _positions.Remove(steamID);
        }

        public void RemovePlayerWeaponStatus(ulong steamID)
        {
            _weaponStatuses.Remove(steamID);
        }

        public void SetPlayerPosition(ulong steamID, PlayerPosition position)
        {
            _positions[steamID] = position;
        }

        public void StopTrackingPlayerWeapon(ulong steamID)
        {
            _weaponStatuses[steamID].IsTracking = false;
        }

        public void StartTrackingAllPlayerWeapon()
        {
            foreach (var status in _weaponStatuses)
            {
                status.Value.IsTracking = true;
            }
        }

        public void TrackPlayerWeapon()
        {
            KillTrackingWeaponTimer();
            _trackingWeaponTimer = _core.Scheduler.RepeatBySeconds(2f, () =>
            {
                foreach (var status in _weaponStatuses)
                {
                    if (!status.Value.IsTracking)
                        continue;

                    status.Value.Weapons.Clear();

                    var player = _gameStateService.Players[status.Key];
                    if (player is null || !player.IsValid || player.PlayerPawn is null || !player.PlayerPawn.IsValid)
                        return;

                    var weaponService = player.PlayerPawn.WeaponServices;
                    if (weaponService is null) return;

                    foreach (var weapon in weaponService.MyWeapons)
                    {
                        if (!weapon.IsValid || weapon.Value is null || weapon.Value.DesignerName.Equals("weapon_c4"))
                            continue;

                        status.Value.Weapons.Add(weapon.Value.DesignerName);
                    }
                }
            });
        }

        public void KillTrackingWeaponTimer()
        {
            _trackingWeaponTimer?.Cancel();
            _trackingWeaponTimer?.Dispose();
            _trackingWeaponTimer = null;
        }

        public void EnableGodMod(int playerSlot)
        {
            var player = _core.PlayerManager
                .GetAllPlayers()
                .FirstOrDefault(f => f.Slot == playerSlot);

            if (player is null || !player.IsValid || player.PlayerPawn is null)
                return;

            player.PlayerPawn.TakesDamage = false;
        }

        public void DisableAllPlayersGodMod()
        {
            var players = _core.PlayerManager
                .GetAllValidPlayers()
                .ToList();

            foreach (var player in players)
            {
                if (player.PlayerPawn is null || !player.PlayerPawn.IsValid)
                    continue;

                player.PlayerPawn.TakesDamage = true;
            }
        }

        public string? GetNameByKeyword(string keyword)
        {
            string normalizedKeyword = keyword.Trim().ToLowerInvariant();
            string targetName = string.Empty;
            int ctr = 0;
            var allPlayers = _core.PlayerManager.GetAllPlayers()
                .Select(s => s.Name)
                .ToList();

            foreach (var playerName in allPlayers)
            {
                if (playerName.Contains(normalizedKeyword, StringComparison.InvariantCultureIgnoreCase))
                {
                    ctr++;
                    targetName = playerName;
                    if (ctr > 1)
                        break;
                }
            }

            return ctr == 1 ? targetName : null;
        }

        public PlayerPosition GetPlayerPosition(ulong steamID) => _positions[steamID];

        public PlayerWeaponStatus GetPlayerWeaponStatus(ulong steamID) => _weaponStatuses[steamID];
    }
}
