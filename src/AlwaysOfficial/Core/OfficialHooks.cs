using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlwaysOfficial.Core;

internal static unsafe class OfficialHooks
{
    private static readonly OfficialSession session = new();
    private static nint currentApp;

    [ThreadStatic] private static bool insideOfficialRequest;
    internal static bool InputsEnabled => !failed && session.Armed;
    internal static bool AutomaticInputTransition => !failed && session.AutomaticRequest;
    private static bool failed = true;

    private static delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void> Run =>
        (delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)Memory.Rebase(0x0047af80);

    private static delegate* unmanaged[Thiscall]<nint, nint*, int, void> Result =>
        (delegate* unmanaged[Thiscall]<nint, nint*, int, void>)Memory.Rebase(0x004d0270);

    private static delegate* unmanaged[Thiscall]<nint, nint*, int, int, void> Request =>
        (delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)Memory.Rebase(0x00490e00);

    private static delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void> Confirm =>
        (delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void>)Memory.Rebase(0x0062da90);

    private readonly record struct Patch(uint Site, uint Target, nint Detour);

    public static bool Install()
    {
        if (sizeof(nint) != 4)
        {
            return false;
        }

        Patch[] patches = [
            new(0x0049cdaf, 0x00491080, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, void>)&ChallengeDetour),
            new(0x00491bc8, 0x0047af80, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)&RunDetour),
            new(0x0049142e, 0x004d0270, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, void>)&ResultDetour),
            new(0x004914fb, 0x004d0270, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, void>)&ResultDetour),
            new(0x004915d3, 0x00490e00, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&RequestDetour),
            new(0x00491e8b, 0x00490e00, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&RequestDetour),
            new(0x004927c6, 0x00490e00, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&RequestDetour),
            new(0x0048b2ba, 0x0062da90, (nint)(delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void>)&ConfirmDetour),
            new(0x008f914a, 0x008f6f50, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, int>)&RestartInputs.SampleDetour),
            new(0x008fc266, 0x008f6fc0, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, int>)&RestartInputs.ButtonEventDetour),
        ];

        foreach (var patch in patches)
        {
            var site = (byte*)Memory.Rebase(patch.Site);

            if (!Memory.Readable(site, 5) || !CallPatch.Matches(new ReadOnlySpan<byte>(site, 5),
                    (uint)(nuint)site, (uint)(nuint)Memory.Rebase(patch.Target)))
            {
                Log.Write($"Unsupported executable or another mod changed CALL {patch.Site:X8}, no hooks installed.");
                return false;
            }
        }

        var installed = 0;

        try
        {
            foreach (var patch in patches)
            {
                var site = (byte*)Memory.Rebase(patch.Site);

                installed++;

                if (!Memory.WriteCode(site, CallPatch.Encode((uint)(nuint)site, (uint)patch.Detour)))
                {
                    throw new InvalidOperationException($"Cannot patch CALL {patch.Site:X8}");
                }
            }
        }
        catch
        {
            foreach (var patch in patches.AsSpan(0, installed))
            {
                var site = (byte*)Memory.Rebase(patch.Site);

                if (!Memory.WriteCode(site, CallPatch.Encode((uint)(nuint)site, (uint)(nuint)Memory.Rebase(patch.Target))))
                {
                    Log.Write($"Failed to restore CALL {patch.Site:X8}, detours remain passive.");
                }
            }

            failed = true;
            throw;
        }

        failed = false;

        Log.Write("Solo official restart hooks installed.");

        return true;
    }

    private static T Read<T>(nint obj, int offset) where T : unmanaged
    {
        return Memory.TryRead((byte*)obj + offset, out T value) ? value : throw new InvalidOperationException("Game object is unreadable");
    }

    private static void Observe(nint app, nint race)
    {
        currentApp = app;
        var challenge = Read<nint>(app, 0x198);
        RestartInputs.Observe(app, race, challenge);
        session.Observe(app, race, challenge, Read<int>(app, 0x268) != 0, Read<int>(app, 0x26c));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void ChallengeDetour(nint app, nint* context)
    {
        try
        {
            if (!failed)
            {
                if (*context == 0)
                {
                    session.Reset();
                    RestartInputs.Reset();
                }

                Observe(app, Read<nint>(app, 0x454));

                // ChallengeMainLoop waits at 0x1927 for the pause menu choice
                if (*context != 0 && *context != -1 && Read<int>(*context, 4) == 0x1927)
                {
                    var menus = Read<nint>(app, 0x194);
                    var choice = Read<int>(menus, 0x700);
                    var next = session.ResultChoice(choice);

                    if (choice != next)
                    {
                        *(int*)((byte*)menus + 0x700) = next;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        ((delegate* unmanaged[Thiscall]<nint, nint*, void>)Memory.Rebase(0x00491080))(app, context);

        if (*context == -1)
        {
            session.Reset();
            RestartInputs.Reset();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void RunDetour(nint race, nint* context, int* status, int flags)
    {
        try
        {
            if (!failed)
            {
                Observe(Read<nint>(race, 0x18), race);
                RestartInputs.PollKeyboard();
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        Run(race, context, status, flags);

        try
        {
            if (failed)
            {
                return;
            }

            var next = session.RaceStatus(*status);

            if (!session.Armed || *status is 1 or 15 or 16)
            {
                RestartInputs.Reset();
            }

            if (next != *status)
            {
                RestartInputs.Remember(currentApp, race, Read<nint>(currentApp, 0x198));

                if (Read<int>(currentApp, 0x268) != 0)
                {
                    ((delegate* unmanaged[Thiscall]<nint, void>)Memory.Rebase(0x004bcc60))(currentApp);
                }

                *status = next;
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void ResultDetour(nint menus, nint* context, int flags)
    {
        Result(menus, context, flags);

        try
        {
            if (failed || currentApp == 0 || *context != -1 || Read<nint>(currentApp, 0x194) != menus)
            {
                return;
            }

            Observe(currentApp, Read<nint>(currentApp, 0x454));

            var choice = Read<int>(menus, 0x700);
            var next = session.ResultChoice(choice);

            if (choice != next)
            {
                *(int*)((byte*)menus + 0x700) = next;
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void RequestDetour(nint app, nint* context, int resetPlayer, int custom)
    {
        var automatic = !failed && session.AutomaticRequest;

        if (automatic)
        {
            custom = session.Custom;
        }

        insideOfficialRequest = automatic;

        try
        {
            Request(app, context, resetPlayer, custom);
        }
        finally
        {
            insideOfficialRequest = false;
        }
        try
        {
            if (automatic && *context == -1)
            {
                var official = Read<int>(app, 0x268) != 0;

                session.RequestCompleted(official);

                if (!official)
                {
                    RestartInputs.Reset();
                }

                Log.Write(official ? "Official retry started." : "Official retry declined or failed; automatic retries disarmed.");
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void ConfirmDetour(nint menus, int allowed, uint cost, uint balance, nint title, nint message)
    {
        try
        {
            if (!failed && insideOfficialRequest && session.SkipConfirmation(allowed != 0))
            {
                // The same choice as DialogConfirmOfficial_OnOk
                // RequestOperation then performs PayRequest
                *(int*)((byte*)menus + 0x700) = 0x1f;
                return;
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        Confirm(menus, allowed, cost, balance, title, message);
    }

    private static void Fail(Exception e)
    {
        failed = true;
        session.Reset();
        RestartInputs.Reset();

        try
        {
            Log.Error("Official hooks disabled for this process", e);
        }
        catch { }
    }
}
