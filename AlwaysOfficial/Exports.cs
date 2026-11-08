using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlwaysOfficial.Core;

namespace AlwaysOfficial;

internal static unsafe class Exports
{
    [UnmanagedCallersOnly(EntryPoint = "ModArchiveEnd", CallConvs = [typeof(CallConvStdcall)])]
    private static void ModArchiveEnd(nint archive, nint node, uint* values, uint count)
    {
        ReplayMetadata.ArchiveEnd(archive, node, values, count);
    }

    [UnmanagedCallersOnly(EntryPoint = "ModBoot", CallConvs = [typeof(CallConvStdcall)])]
    private static int ModBoot(nint module)
    {
        try
        {
            return ModHost.Boot(module) ? 1 : 0;
        }
        catch (Exception e)
        {
            try
            {
                Log.Error("boot", e);
            }
            catch
            {
                Win32.OutputDebugString($"{ModInfo.DisplayName}: boot failed: {e}\n");
            }

            return 0;
        }
    }
}
