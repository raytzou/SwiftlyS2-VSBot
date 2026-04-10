using Microsoft.Extensions.Logging;
using SwiftlyS2.Core.Menus.OptionsBase;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Menus;
using SwiftlyS2.Shared.Players;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Services.Interfaces;

namespace VSBot.Commands
{
    public class Buy : IBuy
    {
        private readonly ILogger<Buy> _logger;
        private readonly ISwiftlyCore _core;
        private readonly IGameStateService _gameStateService;

        public Buy(ILogger<Buy> logger, ISwiftlyCore core, IGameStateService gameStateService)
        {
            _logger = logger;
            _core = core;
            _gameStateService = gameStateService;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            var player = context.Sender;

            if (player is null || !player.IsValid) return;

            var buyTime = _core.ConVar.Find<float>("mp_buytime")!.Value;

            if (!_gameStateService.IsBuyTime)
            {
                context.Reply("[sw] buying time is over!");
                return;
            }

            if (!CanBuy(player))
            {
                context.Reply("[sw] You can't buy anything right now");
                return;
            }

            if (context.Args.Length < 1)
            {
                OpenBuyMenu(player);
                return;
            }

            var buyTarget = context.Args[0].ToLower();
            var weaponSet = WeaponUtility.WeaponMenu
                .Select(weapon => weapon.EntityName);
            var exactMatch = weaponSet
                .FirstOrDefault(w => w.Equals(buyTarget, StringComparison.OrdinalIgnoreCase));
            var fuzzyMatches = weaponSet
                .Where(w => w.Contains(buyTarget, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var targetWeapon = WeaponUtility.ResolveWeaponEntity(buyTarget);
            if (targetWeapon == null)
            {
                context.Reply($"[sw] Invalid weapon: {buyTarget}");
                return;
            }

            var targetPrice = WeaponUtility.WeaponMenu
                .First(weapon => weapon.EntityName == targetWeapon)
                .Price;
            if (!IsMoneyEnough(player, targetPrice))
            {
                context.Reply("[sw] You don't have enough money to buy the item!");
                return;
            }

            BuyItem(player, targetWeapon);
        }

        void OpenBuyMenu(IPlayer player)
        {
            var menuCfg = new MenuConfiguration
            {
                Title = "Buy Menu",
                HideTitle = false,
                HideFooter = false,
                PlaySound = true,
                MaxVisibleItems = 5,
                AutoIncreaseVisibleItems = true,
                FreezePlayer = false,
                AutoCloseAfter = 20f
            };

            var menuBinds = new MenuKeybindOverrides
            {
                Select = KeyBind.E,
                Move = KeyBind.S,
                MoveBack = KeyBind.W,
                Exit = KeyBind.F
            };

            var menu = _core.MenusAPI.CreateMenu(
                configuration: menuCfg,
                keybindOverrides: menuBinds,
                parent: null,
                optionScrollStyle: MenuOptionScrollStyle.LinearScroll,
                optionTextStyle: MenuOptionTextStyle.TruncateEnd
            );

            var closeMenu = new ButtonMenuOption("Close Menu");
            closeMenu.Click += (sender, args) =>
            {
                _core.MenusAPI.CloseMenu(menu);
                return ValueTask.CompletedTask;
            };

            menu.AddOption(closeMenu);

            foreach (var (EntityName, DisplayName, Price) in WeaponUtility.WeaponMenu)
            {
                var option = new ButtonMenuOption($"{DisplayName} ${Price}");

                option.Click += (sender, args) =>
                {
                    _core.Scheduler.NextWorldUpdate(() =>
                    {
                        if (!IsMoneyEnough(player, Price))
                        {
                            player.SendChat("[sw] You don't have enough money to buy the item!");
                        }
                        else
                        {
                            BuyItem(player, EntityName);
                        }
                    });

                    return ValueTask.CompletedTask;
                };

                menu.AddOption(option);
            }

            _core.MenusAPI.OpenMenu(menu);
        }

        bool CanBuy(IPlayer player)
        {
            var maxRound = _core.ConVar.Find<int>("mp_maxrounds")!.Value;

            return player.IsAlive &&
                player.PlayerPawn is not null &&
                player.PlayerPawn.IsValid &&
                player.PlayerPawn.WeaponServices is not null &&
                player.Controller.InGameMoneyServices is not null &&
                _gameStateService.CurrentRound > 0 &&
                _gameStateService.CurrentRound < maxRound;
        }

        bool IsMoneyEnough(IPlayer player, int itemCost) => player.Controller.InGameMoneyServices!.Account >= itemCost;

        void BuyItem(IPlayer player, string item)
        {
            try
            {
                var price = WeaponUtility.WeaponMenu.FirstOrDefault(w => w.EntityName == item).Price;

                WeaponUtility.GiveWeapon(player, item);
                player.Controller.InGameMoneyServices!.Account -= price;
                player.Controller.InGameMoneyServicesUpdated();
            }
            catch (Exception ex)
            {
                player.SendChat("[sw] Failed to buy weapon. Please contact admin!");
                _logger.LogError("Buy Command error {error}", ex);
            }
        }
    }
}
