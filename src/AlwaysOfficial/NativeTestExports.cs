#if NATIVE_TESTS
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlwaysOfficial.Core;

namespace AlwaysOfficial;

internal static unsafe class NativeTestExports
{
    [UnmanagedCallersOnly(EntryPoint = "TestInstall", CallConvs = [typeof(CallConvStdcall)])]
    private static int Install(nint imageBase, nint keyState)
    {
        try
        {
            Memory.TestImageBase = imageBase;
            RestartInputs.TestKeyState = (delegate* unmanaged[Stdcall]<int, short>)keyState;
            return OfficialHooks.Install() ? 1 : 0;
        }
        catch
        {
            return -1;
        }
    }
}
#endif
