using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using System.Globalization;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    internal enum PatchType
    {
        AutoFix,
        Feature
    }

    public class PatchService : IPatchService, IDisposable
    {
        private readonly ISwiftlyCore _core;
        private readonly INativeMemoryService _memory;
        private readonly ILogger<PatchService> _logger;
        private readonly object _lock = new();
        private readonly List<AppliedPatchInfo> _appliedPatches = [];

        private record PatchDefinition(PatchType Type, string Signature, string PatchHex, string ExpectedOriginalHex);
        private record AppliedPatchInfo(string Name, nint Address, byte[] OriginalBytes);

        private const string ServerLibrary = "server";

        private readonly Dictionary<string, PatchDefinition> _definitions = new()
        {
            // cmovle r8d, ebx at VA 0x1802C5DFF incorrectly clamps the bot add counter to quota max on every increment
            ["Fix_BotAdd_Quota_Logic"] = new PatchDefinition(
                Type: PatchType.AutoFix,
                Signature: "44 3B C3 44 0F 4E C3 E8",
                PatchHex: "44 3B C3 90 90 90 90 E8",
                ExpectedOriginalHex: "44 3B C3 44 0F 4E C3 E8"
            )
        };

        public PatchService(ISwiftlyCore core, INativeMemoryService memory, ILogger<PatchService> logger)
        {
            _core = core;
            _memory = memory;
            _logger = logger;
        }

        public void ApplyAllAutoPatches()
        {
            lock (_lock)
            {
                int count = 0;
                foreach (var (name, definition) in _definitions.Where(d => d.Value.Type == PatchType.AutoFix))
                {
                    if (ApplyPatchInternal(name, definition))
                        count++;
                }
                _logger.LogInformation("PatchService: Applied {Count} auto-fix patch(es).", count);
            }
        }

        public bool ApplyPatch(string patchName)
        {
            lock (_lock)
            {
                if (!_definitions.TryGetValue(patchName, out var definition))
                {
                    _logger.LogError("PatchService: Patch definition not found: '{Name}'.", patchName);
                    return false;
                }
                return ApplyPatchInternal(patchName, definition);
            }
        }

        public void RestorePatch(string patchName)
        {
            lock (_lock)
            {
                var patchInfo = _appliedPatches.FirstOrDefault(p => p.Name == patchName);
                if (patchInfo is null)
                {
                    _logger.LogWarning("PatchService: Cannot restore patch '{Name}': not currently applied.", patchName);
                    return;
                }

                try
                {
                    if (_memory.SetMemoryProtection(patchInfo.Address, patchInfo.OriginalBytes.Length))
                        _memory.WriteBytes(patchInfo.Address, patchInfo.OriginalBytes);
                    else
                        _logger.LogError("PatchService: Failed to restore patch '{Name}': memory protection change failed.", patchName);

                    _appliedPatches.Remove(patchInfo);
                    _logger.LogInformation("PatchService: Restored patch '{Name}'.", patchName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PatchService: Exception while restoring patch '{Name}'.", patchName);
                }
            }
        }

        public void RestoreAll()
        {
            lock (_lock)
            {
                if (_appliedPatches.Count == 0) return;

                for (int i = _appliedPatches.Count - 1; i >= 0; i--)
                {
                    var patch = _appliedPatches[i];
                    _memory.SetMemoryProtection(patch.Address, patch.OriginalBytes.Length);
                    _memory.WriteBytes(patch.Address, patch.OriginalBytes);
                }
                _appliedPatches.Clear();
                _logger.LogInformation("PatchService: All patches restored.");
            }
        }

        public void Dispose()
        {
            RestoreAll();
            GC.SuppressFinalize(this);
        }

        private bool ApplyPatchInternal(string patchName, PatchDefinition definition)
        {
            if (_appliedPatches.Any(p => p.Name == patchName))
                return true;

            try
            {
                var address = _core.Memory.GetAddressBySignature(ServerLibrary, definition.Signature);
                if (address is null || address.Value == nint.Zero)
                {
                    _logger.LogError("PatchService: Signature scan failed for patch '{Name}'.", patchName);
                    return false;
                }

                var patchBytes = ParseHexString(definition.PatchHex);
                var expectedTokenCount = definition.ExpectedOriginalHex
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                int readLength = Math.Max(patchBytes.Length, expectedTokenCount);

                var currentBytes = _memory.ReadBytes(address.Value, readLength);
                if (!ValidateBytes(currentBytes, definition.ExpectedOriginalHex))
                {
                    _logger.LogCritical(
                        "PatchService: Validation failed for patch '{Name}'. Memory content does not match expected pattern. Aborting to prevent crash.",
                        patchName);
                    return false;
                }

                var originalBytes = new byte[patchBytes.Length];
                Array.Copy(currentBytes, 0, originalBytes, 0, patchBytes.Length);

                if (!_memory.SetMemoryProtection(address.Value, patchBytes.Length))
                {
                    _logger.LogError("PatchService: Failed to change memory protection for patch '{Name}'.", patchName);
                    return false;
                }

                _memory.WriteBytes(address.Value, patchBytes);
                _appliedPatches.Add(new AppliedPatchInfo(patchName, address.Value, originalBytes));
                _logger.LogInformation("PatchService: Applied patch '{Name}' (Type: {Type}).", patchName, definition.Type);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PatchService: Exception while applying patch '{Name}'.", patchName);
                return false;
            }
        }

        private static byte[] ParseHexString(string hexString) =>
            hexString.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Select(token => token == "?" ? (byte)0 : byte.Parse(token, NumberStyles.HexNumber))
                     .ToArray();

        private static bool ValidateBytes(byte[] currentBytes, string expectedPattern)
        {
            var tokens = expectedPattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (currentBytes.Length < tokens.Length) return false;

            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i] == "?") continue;
                if (currentBytes[i] != byte.Parse(tokens[i], NumberStyles.HexNumber)) return false;
            }
            return true;
        }
    }
}
