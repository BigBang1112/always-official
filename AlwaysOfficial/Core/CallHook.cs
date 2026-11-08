namespace AlwaysOfficial.Core;

internal readonly unsafe record struct CallHook(uint Site, uint Target, nint Detour)
{
    private byte* SitePointer => (byte*)Memory.Rebase(Site);
    private uint TargetPointer => (uint)(nuint)Memory.Rebase(Target);

    private bool MatchesOriginal()
    {
        var site = SitePointer;
        return Memory.Readable(site, 5) &&
            CallPatch.Matches(new ReadOnlySpan<byte>(site, 5), (uint)(nuint)site, TargetPointer);
    }

    private bool Write(uint target)
    {
        var site = SitePointer;
        return Memory.WriteCode(site, CallPatch.Encode((uint)(nuint)site, target));
    }

    public static bool Install(ReadOnlySpan<CallHook> hooks)
    {
        // Validate the entire set before changing any instruction
        foreach (var hook in hooks)
        {
            if (!hook.MatchesOriginal())
            {
                Log.Write($"Unsupported executable or another mod changed CALL {hook.Site:X8}, no hooks installed.");
                return false;
            }
        }

        var attempted = 0;
        try
        {
            foreach (var hook in hooks)
            {
                // WriteCode can throw after writing, so include the current hook in rollback
                attempted++;
                if (!hook.Write((uint)hook.Detour))
                {
                    throw new InvalidOperationException($"Cannot patch CALL {hook.Site:X8}");
                }
            }
        }
        catch
        {
            foreach (var hook in hooks[..attempted])
            {
                if (!hook.Write(hook.TargetPointer))
                {
                    Log.Write($"Failed to restore CALL {hook.Site:X8}, detours remain passive.");
                }
            }
            throw;
        }

        return true;
    }
}
