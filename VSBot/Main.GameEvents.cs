using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.GameEvents;
using SwiftlyS2.Shared.Misc;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using VSBot.Classes;
using VSBot.Commands.Interfaces;
using VSBot.Models;
using VSBot.Modules;
using VSBot.Services.Interfaces;
using static SwiftlyS2.Shared.Helper;

namespace VSBot
{
    public partial class Main
    {
        [GameEventHandler(HookMode.Post)]
        public HookResult PlayerConnectFullHandler(EventPlayerConnectFull @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid) return HookResult.Continue;

            if (!player.IsFakeClient)
            {
                var gameState = GetRequiredService<IGameStateService>();
                var playerService = GetRequiredService<IPlayerService>();

                gameState.AddPlayer(player);
                playerService.AddPlayerPosition(player.SteamID);
                playerService.AddPlayerWeaponStatus(player.SteamID);
                SendConnectedMessage(gameState.Players.Values.ToList());
                Core.Logger.LogInformation("{client} has connected at {DT}, IP: {ipAddress}, SteamID: {steamID}", player.Name, DateTime.Now, player.IPAddress, player.SteamID);
            }

            return HookResult.Continue;

            void SendConnectedMessage(List<IPlayer> players)
            {
                foreach (var thePlayer in players)
                {
                    thePlayer.SendChat($" {ChatColors.Lime}{player.Name} {Utility.GetTeamColorByPlayer(thePlayer)}has connected to the server.");
                }
            }
        }

        [GameEventHandler(HookMode.Pre)]
        public HookResult PlayerDisconnectHandler(EventPlayerDisconnect @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid) return HookResult.Continue;

            if (!player.IsFakeClient)
            {
                var gameState = GetRequiredService<IGameStateService>();
                var playerService = GetRequiredService<IPlayerService>();
                var reviveCommand = GetRequiredService<IRevive>();

                gameState.RemovePlayer(player.SteamID);
                playerService.RemovePlayerPosition(player.SteamID);
                playerService.RemovePlayerWeaponStatus(player.SteamID);
                reviveCommand.OnPlayerDisconnect(player.SteamID);
                Core.Logger.LogInformation("{client} has disconnected at {DT}", player.Name, DateTime.Now);
            }

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult RoundAnnounceWarmupHandler(EventRoundAnnounceWarmup @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnWarmupStarted();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult WarmupEndHandler(EventWarmupEnd @event)
        {
            var gameState = GetRequiredService<IGameStateService>();
            var botModule = GetRequiredService<BotModule>();

            gameState.OnWarmupEnded();
            botModule.OnWarmupEnd();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult RoundStartHandler(EventRoundStart @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnBuyTimeStart();
            gameState.OnRoundStart();

            if (gameState.IsWarmup) return HookResult.Continue;

            var playerService = GetRequiredService<IPlayerService>();

            playerService.StartTrackingAllPlayerWeapon();
            playerService.TrackPlayerWeapon();
            playerService.ApplyRoundStartHealth();

            if (gameState.CurrentRound > 0)
            {
                playerService.DisableAllPlayersGodMod();
            }

            var botModule = GetRequiredService<BotModule>();

            botModule.OnRoundStart();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult RoundEndHandler(EventRoundEnd @event)
        {
            var gameState = GetRequiredService<IGameStateService>();
            var playerService = GetRequiredService<IPlayerService>();
            var reviveCommand = GetRequiredService<IRevive>();
            var botModule = GetRequiredService<BotModule>();
            var winTeam = @event.Winner;

            gameState.OnRoundEnd();
            gameState.CalculateStreak(winTeam);
            playerService.KillTrackingWeaponTimer();
            reviveCommand.OnRoundEnd();
            botModule.OnRoundEnd();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult BuytimeEndedHandler(EventBuytimeEnded @event)
        {
            var gameState = GetRequiredService<IGameStateService>();

            gameState.OnBuyTimeEnd();

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult PlayerDeathHandler(EventPlayerDeath @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid || player.PlayerPawn is null || !player.PlayerPawn.IsValid)
                return HookResult.Continue;

            var gameState = GetRequiredService<IGameStateService>();
            if (gameState.IsWarmup || gameState.IsRoundEnd) return HookResult.Continue;

            if (!player.IsFakeClient)
            {
                SavePlayerDeathPosition(player);
            }

            var botModule = GetRequiredService<BotModule>();

            botModule.RespawnBot(player);

            return HookResult.Continue;
        }

        [GameEventHandler(HookMode.Post)]
        public HookResult PlayerSpawnHandler(EventPlayerSpawn @event)
        {
            var player = @event.UserIdPlayer;
            if (player is null || !player.IsValid)
                return HookResult.Continue;

            var gameState = GetRequiredService<IGameStateService>();
            var playerService = GetRequiredService<IPlayerService>();

            if (gameState.CurrentRound == 0)
            {
                playerService.EnableGodMod(player.Slot);
            }

            if (!player.IsFakeClient)
            {
                playerService.ApplySpawnHealth(player);
            }

            return HookResult.Continue;
        }

        private void SavePlayerDeathPosition(IPlayer player)
        {
            if (player.PlayerPawn is null)
                return;

            var origin = new Vector(player.PlayerPawn.AbsOrigin?.X ?? 0f, player.PlayerPawn.AbsOrigin?.Y ?? 0f, player.PlayerPawn.AbsOrigin?.Z ?? 0f);
            var rotation = new QAngle(player.PlayerPawn.AbsRotation?.X ?? 0f, player.PlayerPawn.AbsRotation?.Y ?? 0f, player.PlayerPawn.AbsRotation?.Z ?? 0f);
            var velocity = new Vector(player.PlayerPawn.AbsVelocity.X, player.PlayerPawn.AbsVelocity.Y, player.PlayerPawn.AbsVelocity.Z);
            var playerService = GetRequiredService<IPlayerService>();
            var position = new PlayerPosition
            {
                Origin = origin,
                Rotation = rotation,
                Velocity = velocity
            };

            playerService.SetPlayerPosition(player.SteamID, position);
            playerService.StopTrackingPlayerWeapon(player.SteamID);
        }
    }
}
