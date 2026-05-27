using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using VSBot.Services.Interfaces;

namespace VSBot.Services
{
    public class NativeMemoryService : INativeMemoryService
    {
        private readonly ILogger<NativeMemoryService> _logger;
        private readonly ConcurrentDictionary<nint, int> _linuxAllocations = new();

        // Windows VirtualAlloc/VirtualFree/VirtualProtect flags
        private const uint MemCommit = 0x1000;
        private const uint MemReserve = 0x2000;
        private const uint MemRelease = 0x8000;
        private const uint PageExecuteReadWrite = 0x40;

        // Linux mprotect protection flags
        private const int ProtRead = 0x1;
        private const int ProtWrite = 0x2;
        private const int ProtExec = 0x4;

        // MAP_PRIVATE | MAP_ANONYMOUS
        private const int MapPrivateAnonymous = 0x22;

        private const int OsPageSize = 4096;

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        private static extern nint VirtualAlloc(nint lpAddress, int dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(nint lpAddress, int dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtect(nint lpAddress, int dwSize, int flNewProtect, out int lpflOldProtect);

        [DllImport("libc", EntryPoint = "mmap")]
        private static extern nint MMap(nint addr, int length, int prot, int flags, int fd, long offset);

        [DllImport("libc", EntryPoint = "munmap")]
        private static extern int MUnmap(nint addr, int length);

        [DllImport("libc", EntryPoint = "mprotect", SetLastError = true)]
        private static extern int MProtect(nint addr, int len, int prot);

        public NativeMemoryService(ILogger<NativeMemoryService> logger)
        {
            _logger = logger;
        }

        public nint AllocateMemory(int size)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var ptr = VirtualAlloc(nint.Zero, size, MemCommit | MemReserve, PageExecuteReadWrite);
                    if (ptr == nint.Zero)
                        _logger.LogError("NativeMemoryService: VirtualAlloc failed. Win32 error: {Error}.", Marshal.GetLastWin32Error());
                    return ptr;
                }

                if (OperatingSystem.IsLinux())
                {
                    var ptr = MMap(nint.Zero, size, ProtRead | ProtWrite | ProtExec, MapPrivateAnonymous, -1, 0);
                    if (ptr == (nint)(-1))
                    {
                        _logger.LogError("NativeMemoryService: mmap failed.");
                        return nint.Zero;
                    }
                    _linuxAllocations[ptr] = size;
                    return ptr;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NativeMemoryService: Exception during AllocateMemory.");
            }
            return nint.Zero;
        }

        public void FreeMemory(nint address)
        {
            if (address == nint.Zero) return;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    if (!VirtualFree(address, 0, MemRelease))
                        _logger.LogWarning("NativeMemoryService: VirtualFree failed. Win32 error: {Error}.", Marshal.GetLastWin32Error());
                }
                else if (OperatingSystem.IsLinux() && _linuxAllocations.TryRemove(address, out int size))
                {
                    MUnmap(address, size);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NativeMemoryService: Exception during FreeMemory.");
            }
        }

        public bool SetMemoryProtection(nint address, int size)
        {
            if (address == nint.Zero) return false;
            try
            {
                if (OperatingSystem.IsWindows())
                    return VirtualProtect(address, size, (int)PageExecuteReadWrite, out _);

                if (OperatingSystem.IsLinux())
                {
                    // mprotect requires a page-aligned base address
                    var aligned = address & ~(nint)(OsPageSize - 1);
                    var alignedSize = size + (int)(address - aligned);
                    return MProtect(aligned, alignedSize, ProtRead | ProtWrite | ProtExec) == 0;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NativeMemoryService: Exception during SetMemoryProtection.");
            }
            return false;
        }

        public void WriteBytes(nint address, byte[] data)
        {
            if (address == nint.Zero || data is null || data.Length == 0) return;
            Marshal.Copy(data, 0, address, data.Length);
        }

        public void WriteInt32(nint address, int offset, int value)
        {
            if (address == nint.Zero) return;
            Marshal.WriteInt32(address, offset, value);
        }

        public void WriteIntPtr(nint address, int offset, nint value)
        {
            if (address == nint.Zero) return;
            Marshal.WriteIntPtr(address, offset, value);
        }

        public byte[] ReadBytes(nint address, int length)
        {
            if (address == nint.Zero || length <= 0) return [];
            var buffer = new byte[length];
            Marshal.Copy(address, buffer, 0, length);
            return buffer;
        }
    }
}
