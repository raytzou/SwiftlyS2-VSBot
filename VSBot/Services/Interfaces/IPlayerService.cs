using VSBot.Models;

namespace VSBot.Services.Interfaces
{
    public interface IPlayerService
    {
        void AddPlayerPosition(ulong steamID);
        void RemovePlayerPosition(ulong steamID);
        void AddPlayerWeaponStatus(ulong steamID);
        void RemovePlayerWeaponStatus(ulong steamID);
        void ClearPlayerPositions();
        void ClearPlayerWeaponStatuses();
        void SetPlayerPosition(ulong steamID, PlayerPosition position);
        void StopTrackingPlayerWeapon(ulong steamID);
        void StartTrackingAllPlayerWeapon();
        void TrackPlayerWeapon();
        void KillTrackingWeaponTimer();
        void EnableGodMod(int playerSlot);
        void DisableAllPlayersGodMod();
        PlayerPosition GetPlayerPosition(ulong steamID);
        PlayerWeaponStatus GetPlayerWeaponStatus(ulong steamID);
    }
}
