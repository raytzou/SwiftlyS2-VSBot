using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using VSBot.Commands.Interfaces;

namespace VSBot.Commands
{
    public class Cvar : ICvar
    {
        private readonly ILogger<Cvar> _logger;
        private readonly ISwiftlyCore _core;

        public Cvar(ILogger<Cvar> logger, ISwiftlyCore core)
        {
            _logger = logger;
            _core = core;
        }

        public void ExecuteCommand(ICommandContext context)
        {
            if (context.Args.Length < 1)
            {
                context.Reply("[sw] Usage: sw_cvar <ConVar> <Value>");
                return;
            }

            var cvarName = context.Args[0];
            var adminName = context.Sender?.Name ?? "Console";

            if (context.Args.Length == 1)
            {
                DisplayConVarValue(context, cvarName);
                return;
            }

            var newValueArg = context.Args[1];
            TrySetConVarValue(context, cvarName, newValueArg, adminName);
        }

        private void DisplayConVarValue(ICommandContext context, string cvarName)
        {
            try
            {
                if (TryGetConVarValue<bool>(cvarName, out var boolValue))
                {
                    context.Reply($"{cvarName}: {boolValue}");
                    return;
                }

                if (TryGetConVarValue<int>(cvarName, out var intValue))
                {
                    context.Reply($"{cvarName}: {intValue}");
                    return;
                }

                if (TryGetConVarValue<float>(cvarName, out var floatValue))
                {
                    context.Reply($"{cvarName}: {floatValue}");
                    return;
                }

                if (TryGetConVarValue<string>(cvarName, out var stringValue))
                {
                    context.Reply($"{cvarName}: {stringValue}");
                    return;
                }

                context.Reply($"[sw] Cannot find the ConVar: {cvarName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get ConVar value: {cvarName}", cvarName);
                context.Reply($"[sw] Failed to retrieve value for ConVar: {cvarName}");
            }
        }

        private bool TryGetConVarValue<T>(string cvarName, out T value)
        {
            value = default!;
            try
            {
                var cvar = _core.ConVar.Find<T>(cvarName);
                if (cvar != null)
                {
                    value = cvar.Value;
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private void TrySetConVarValue(ICommandContext context, string cvarName, string newValueArg, string adminName)
        {
            try
            {
                if (TryGetConVarValue<bool>(cvarName, out _))
                {
                    var boolValue = ParseBooleanValue(newValueArg);
                    if (boolValue == null)
                    {
                        context.Reply($"[sw] Invalid boolean value. Use: 0/1/true/false");
                        return;
                    }

                    var boolCvar = _core.ConVar.Find<bool>(cvarName);
                    if (boolCvar != null)
                    {
                        boolCvar.Value = boolValue.Value;
                        LogAndBroadcastChange(cvarName, boolValue.Value.ToString().ToLower(), adminName);
                        return;
                    }
                }

                if (TryGetConVarValue<int>(cvarName, out _))
                {
                    if (!int.TryParse(newValueArg, out int intValue))
                    {
                        context.Reply($"[sw] Invalid integer value");
                        return;
                    }

                    var intCvar = _core.ConVar.Find<int>(cvarName);
                    if (intCvar != null)
                    {
                        intCvar.Value = intValue;
                        LogAndBroadcastChange(cvarName, intValue.ToString(), adminName);
                        return;
                    }
                }

                if (TryGetConVarValue<float>(cvarName, out _))
                {
                    if (!float.TryParse(newValueArg, out float floatValue))
                    {
                        context.Reply($"[sw] Invalid float value");
                        return;
                    }

                    var floatCvar = _core.ConVar.Find<float>(cvarName);
                    if (floatCvar != null)
                    {
                        floatCvar.Value = floatValue;
                        LogAndBroadcastChange(cvarName, floatValue.ToString(), adminName);
                        return;
                    }
                }

                if (TryGetConVarValue<string>(cvarName, out _))
                {
                    var stringCvar = _core.ConVar.Find<string>(cvarName);
                    if (stringCvar != null)
                    {
                        stringCvar.Value = newValueArg;
                        LogAndBroadcastChange(cvarName, newValueArg, adminName);
                        return;
                    }
                }

                context.Reply($"[sw] Cannot find the ConVar: {cvarName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set ConVar: {cvarName} to value: {value}", cvarName, newValueArg);
                context.Reply($"[sw] Failed to set ConVar value. Check server logs for details.");
            }
        }

        private bool? ParseBooleanValue(string value)
        {
            return value switch
            {
                "0" => false,
                "1" => true,
                "false" or "False" or "FALSE" => false,
                "true" or "True" or "TRUE" => true,
                _ => null
            };
        }

        private void LogAndBroadcastChange(string cvarName, string newValue, string adminName)
        {
            _core.PlayerManager.SendChat($"{cvarName} changed to {newValue}");
            _logger.LogInformation("{admin} changed {cvar} to {value} at {DT}",
                adminName, cvarName, newValue, DateTime.Now);
        }
    }
}
