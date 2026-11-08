namespace AlwaysOfficial.Core;

internal static class ModHost
{
    private static nint instanceMutex;

    public static bool Boot(nint module)
    {
        instanceMutex = Win32.CreateMutex(0, false, $"Local\\{ModInfo.Name}_{Win32.GetCurrentProcessId()}");
        if (instanceMutex == 0)
        {
            return false;
        }

        if (System.Runtime.InteropServices.Marshal.GetLastPInvokeError() == Win32.ERROR_ALREADY_EXISTS)
        {
            Win32.CloseHandle(instanceMutex);
            return false;
        }

        Log.Init(ModInfo.Name);
        Log.Write($"{ModInfo.DisplayName} {ModInfo.Version}: {Win32.ModulePath(0)}");

        return Mod.Install();
    }
}
