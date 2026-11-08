using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlwaysOfficial.Core;

namespace AlwaysOfficial;

internal static class Exports
{
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
