namespace AlwaysOfficial.Core;

internal static unsafe class Memory
{
#if NATIVE_TESTS
    internal static nint TestImageBase;

    public static void* Rebase(nuint address)
    {
        return (void*)((nuint)(TestImageBase != 0 ? TestImageBase : Win32.GetModuleHandle(null)) - 0x00400000 + address);
    }
#else
    public static void* Rebase(nuint address)
    {
        return (void*)((nuint)Win32.GetModuleHandle(null) - 0x00400000 + address);
    }
#endif

    public static bool Readable(void* address, int size)
    {
        Win32.MEMORY_BASIC_INFORMATION region;
        if (size < 0 || Win32.VirtualQuery(address, &region, (nuint)sizeof(Win32.MEMORY_BASIC_INFORMATION)) == 0)
        {
            return false;
        }

        return region.State == Win32.MEM_COMMIT && (region.Protect & (Win32.PAGE_NOACCESS | Win32.PAGE_GUARD)) == 0 &&
            (nuint)address >= (nuint)region.BaseAddress &&
            (nuint)address + (nuint)size >= (nuint)address &&
            (nuint)address + (nuint)size <= (nuint)region.BaseAddress + (nuint)region.RegionSize;
    }

    public static bool TryRead<T>(void* address, out T value) where T : unmanaged
    {
        value = default;
        if (!Readable(address, sizeof(T)))
        {
            return false;
        }

        value = *(T*)address;
        return true;
    }

    public static bool WriteCode(byte* address, ReadOnlySpan<byte> bytes)
    {
        uint old;

        if (!Win32.VirtualProtect(address, (nuint)bytes.Length, Win32.PAGE_EXECUTE_READWRITE, &old))
        {
            return false;
        }

        bytes.CopyTo(new Span<byte>(address, bytes.Length));
        uint ignored;
        var restored = Win32.VirtualProtect(address, (nuint)bytes.Length, old, &ignored);
        var flushed = Win32.FlushInstructionCache(Win32.GetCurrentProcess(), address, (nuint)bytes.Length);

        if (!restored || !flushed)
        {
            throw new InvalidOperationException("Code written but protection/cache update failed");
        }

        return true;
    }
}
