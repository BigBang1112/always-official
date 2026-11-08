using System.Runtime.InteropServices;

namespace AlwaysOfficial.Core;

internal static unsafe partial class Win32
{
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint GetProcAddress(nint module, string name);

    public const uint PAGE_EXECUTE_READWRITE = 0x40;
    public const uint PAGE_NOACCESS = 0x01;
    public const uint PAGE_GUARD = 0x100;
    public const uint MEM_COMMIT = 0x1000;
    public const int ERROR_ALREADY_EXISTS = 183;

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [LibraryImport("kernel32.dll")]
    public static partial void GetLocalTime(out SYSTEMTIME time);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int virtualKey);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    public static partial uint MapVirtualKey(uint code, uint mapType);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualProtect(void* address, nuint size, uint newProtect, uint* oldProtect);

    [LibraryImport("kernel32.dll")]
    public static partial nuint VirtualQuery(void* address, MEMORY_BASIC_INFORMATION* buffer, nuint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandle(string? name);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateMutexW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial nint CreateMutex(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool initialOwner, string name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentProcessId();

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FlushInstructionCache(nint process, void* address, nuint length);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleFileNameW")]
    public static partial uint GetModuleFileName(nint module, char* buffer, uint size);

    [LibraryImport("kernel32.dll", EntryPoint = "OutputDebugStringW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial void OutputDebugString(string text);

    public static string ModulePath(nint module)
    {
        var buffer = stackalloc char[520];
        var n = GetModuleFileName(module, buffer, 520);

        return new string(buffer, 0, (int)n);
    }
}
