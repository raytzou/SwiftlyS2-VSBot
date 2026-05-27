using SwiftlyS2.Shared.Helpers;
using SwiftlyS2.Shared.Players;
using System.Text.RegularExpressions;

namespace VSBot.Classes
{
    public class WeaponUtility
    {
        public static readonly IReadOnlyList<(string EntityName, string DisplayName, int Price)> WeaponMenu = new List<(string EntityName, string DisplayName, int Price)>()
        {
            // pistols
            {("weapon_glock", "Glock-18", 200)},
            {("weapon_hkp2000", "P2000", 200)},
            {("weapon_usp_silencer", "USP-S", 200)},
            {("weapon_elite", "Dual Berettas", 300)},
            {("weapon_p250", "P250", 300)},
            {("weapon_tec9", "TEC-9", 500)},
            {("weapon_cz75a", "CZ75-Auto", 500)},
            {("weapon_fiveseven", "Five-SeveN", 500)},
            {("weapon_deagle", "Desert Eagle", 700)},
            {("weapon_revolver", "R8 Revolver ", 600)},
            // SMGs
            {("weapon_mac10", "MAC-10", 1050)},
            {("weapon_mp9", "MP9", 1250)},
            {("weapon_mp7", "MP7", 1500)},
            {("weapon_mp5sd", "MP5-SD", 1500)},
            {("weapon_ump45", "UMP-45", 1200)},
            {("weapon_p90", "P90", 2350)},
            {("weapon_bizon", "PP-Bizon", 1400)},
            // heavy weapons
            {("weapon_nova", "Nova", 1050)},
            {("weapon_xm1014", "XM1014", 2000)},
            {("weapon_sawedoff", "Sawed-Off", 1100)},
            {("weapon_mag7", "MAG-7", 1300)},
            {("weapon_m249", "M249", 5200)},
            {("weapon_negev", "Negev", 1700)},
            // rifles
            {("weapon_galil", "Galil AR", 1800)},
            {("weapon_famas", "FAMAS", 1950)},
            {("weapon_ak47", "AK-47", 2700)},
            {("weapon_m4a1", "M4A4", 2900)},
            {("weapon_m4a1_silencer", "M4A1-S", 2900) },
            {("weapon_ssg08", "SSG 08", 1700)},
            {("weapon_sg556", "SG 553", 3000)},
            {("weapon_aug", "AUG", 3300)},
            {("weapon_awp", "AWP", 4750)},
            {("weapon_g3sg1", "G3SG1", 5000)},
            {("weapon_scar20", "SCAR-20", 5000)},
            // others
            {("weapon_healthshot", "Medi-Shot", 500)},
            //{("weapon_shield", "Riot Shield", 800)},
            //{("weapon_tagrenade", "Tactical Awareness Grenade", 200)},
            {("weapon_c4", "C4", 16000)},
        };

        private static readonly Dictionary<string, string> _weaponNameMappings = new()
        {
            ["weapon_m4a1_silencer"] = "weapon_m4a1",
            ["m4a1-s"] = "weapon_m4a1_silencer",
            ["m4a1s"] = "weapon_m4a1_silencer",
            ["m4a1"] = "weapon_m4a1_silencer",
            ["m4a4"] = "weapon_m4a1",
            ["usp-s"] = "weapon_usp_silencer",
            ["usps"] = "weapon_usp_silencer",
            ["usp"] = "weapon_usp_silencer",
        };

        public static string? ResolveWeaponEntity(string input)
        {
            var normalized = input.ToLowerInvariant().Trim();

            // Check if it's a direct alias
            if (_weaponNameMappings.TryGetValue(normalized, out var mapped))
                return mapped;

            // Check if it's already a valid entity name
            if (WeaponMenu.Any(w => w.EntityName.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                return normalized;

            // Fuzzy match on entity names (existing behavior)
            var matches = WeaponMenu
                .Where(w => w.EntityName.Contains(normalized, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.Count == 1 ? matches[0].EntityName : null;
        }

        public static void GiveWeapon(IPlayer? player, string weaponEntity)
        {
            if (player is null || !player.IsAlive)
                throw new ArgumentNullException("Player is null or invalid");
            if (string.IsNullOrEmpty(weaponEntity))
                throw new ArgumentException("Weapon Entity is null or empty");
            if (player.PlayerPawn is null || !player.PlayerPawn.IsValid)
                throw new InvalidOperationException($"PlayerPawn is null or invalid");

            var playerWeaponService = player.PlayerPawn.WeaponServices
                ?? throw new NullReferenceException("Player Weapon Service is null");
            var pistols = WeaponMenu
                .Take(10)
                .Select(weapon => weapon.EntityName)
                .ToHashSet();
            var mainWeapons = WeaponMenu
                .Skip(10)
                .Take(24)
                .Select(weapon => weapon.EntityName)
                .ToHashSet();
            var others = WeaponMenu
                .Skip(34)
                .Take(2)
                .Select(weapon => weapon.EntityName)
                .ToHashSet();

            if (!pistols.Contains(weaponEntity) && !mainWeapons.Contains(weaponEntity) && !others.Contains(weaponEntity))
                throw new ArgumentException("Weapon Entity is not found");

            if (!others.Contains(weaponEntity))
            {
                var playerWeapons = playerWeaponService.MyWeapons;
                bool isPistol = pistols.Contains(weaponEntity);

                foreach (var weapon in playerWeapons)
                {
                    if (!weapon.IsValid || weapon.Value is null)
                        continue;

                    var weaponsToCheck = isPistol ? pistols : mainWeapons;

                    if (weaponsToCheck.Contains(weapon.Value.DesignerName))
                        player.PlayerPawn.WeaponServices.DropWeapon(weapon.Value);
                }
            }

            if (player.PlayerPawn.ItemServices is null || !player.PlayerPawn.ItemServices.IsValid)
                throw new InvalidOperationException("ItemService is null or invalid");

            player.PlayerPawn.ItemServices.GiveItem(weaponEntity);
        }

        public static string? GetEntityNameByIndex(ItemDefinitionIndex index)
        {
            if (index == ItemDefinitionIndex.Galilar)
                return "weapon_galil";

            if (index == ItemDefinitionIndex.HkP2000)
                return "weapon_hkp2000";

            var enumName = index.ToString();
            var snakeCase = Regex.Replace(enumName, @"(?<=[a-z0-9])(?=[A-Z])", "_").ToLowerInvariant();
            var entity = $"weapon_{snakeCase}";

            return WeaponMenu.Any(w => w.EntityName.Equals(entity, StringComparison.OrdinalIgnoreCase))
                ? entity
                : null;
        }
    }
}
