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

    public static bool Install()
    {
        if (sizeof(nint) != 4)
        {
            return false;
        }

        // Patch individual CALL sites so unrelated callers retain the game's behavior
        // In particular, only the normal solo Run call is wrapped, not cleanup Run calls
        CallHook[] patches = [
            new(0x0049cdaf, GameFunctions.ChallengeMainLoopAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, void>)&ChallengeDetour),
            new(0x00491bc8, GameFunctions.RunAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)&RunDetour),
            new(0x0049142e, GameFunctions.ChallengeResultAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, void>)&ResultDetour),
            new(0x004914fb, GameFunctions.ChallengeResultAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, void>)&ResultDetour),
            new(0x004915d3, GameFunctions.RequestOfficialRaceAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&RequestDetour),
            new(0x00491e8b, GameFunctions.RequestOfficialRaceAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&RequestDetour),
            new(0x004927c6, GameFunctions.RequestOfficialRaceAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&RequestDetour),
            new(0x0048b2ba, GameFunctions.ConfirmOfficialAddress, (nint)(delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void>)&ConfirmDetour),
            new(0x008f914a, GameFunctions.SetInputStateAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, int>)&RestartInputs.SampleDetour),
            new(0x008fc266, GameFunctions.InsertInputEventAddress, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, int>)&RestartInputs.ButtonEventDetour),
        ];

        try
        {
            if (!CallHook.Install(patches))
            {
                return false;
            }
        }
        catch
        {
            failed = true;
            throw;
        }

        failed = false;

        Log.Write("Solo official restart hooks installed.");

        return true;
    }

    private static void Observe(nint app, nint race)
    {
        // CTrackMania: +0x198 challenge, +0x454 race, +0x268 official active, +0x26c custom official mode
        // Preserve the mode when requesting another run
        currentApp = app;
        var challenge = Memory.ReadField<nint>(app, 0x198);
        RestartInputs.Observe(app, race, challenge);
        session.Observe(app, race, challenge, Memory.ReadField<int>(app, 0x268) != 0, Memory.ReadField<int>(app, 0x26c));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void ChallengeDetour(nint app, nint* context)
    {
        // SMwFiberContext*: null starts a fiber, -1 marks completion, +4 holds its resume state
        // These are cooperative calls, so one invocation need not complete the operation
        try
        {
            if (!failed)
            {
                if (*context == 0)
                {
                    ResetSession();
                }

                Observe(app, Memory.ReadField<nint>(app, 0x454));

                // ChallengeMainLoop waits at 0x1927 for the pause menu choice
                // app+0x194 is the menu manager, translate its choice before the game consumes it
                if (*context != 0 && *context != -1 && Memory.ReadField<int>(*context, 4) == 0x1927)
                {
                    TranslateRetryChoice(Memory.ReadField<nint>(app, 0x194));
                }
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        GameFunctions.ChallengeMainLoop(app, context);

        if (*context == -1)
        {
            ResetSession();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void RunDetour(nint race, nint* context, int* status, int flags)
    {
        try
        {
            if (!failed)
            {
                Observe(Memory.ReadField<nint>(race, 0x18), race);
                RestartInputs.PollKeyboard();
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        // Run copies CGameRace's status at +0x50 to status. OfficialSession changes armed
        // retry statuses 3/4 to 13 so ChallengeMainLoop takes its official-request branch.
        GameFunctions.Run(race, context, status, flags);

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
                RestartInputs.Remember(currentApp, race, Memory.ReadField<nint>(currentApp, 0x198));

                if (Memory.ReadField<int>(currentApp, 0x268) != 0)
                {
                    // CTrackMania::CancelOfficialRecord -> DoStopOfficialRecord(-2, -2)
                    // stop the previous official recording before starting its replacement
                    GameFunctions.CancelOfficialRecord(currentApp);
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
        // Both result-dialog CALL sites use this hook
        GameFunctions.ChallengeResult(menus, context, flags);

        try
        {
            if (failed || currentApp == 0 || *context != -1 || Memory.ReadField<nint>(currentApp, 0x194) != menus)
            {
                return;
            }

            Observe(currentApp, Memory.ReadField<nint>(currentApp, 0x454));

            TranslateRetryChoice(menus);
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

        // RequestOperation is shared with other operations
        insideOfficialRequest = automatic;

        try
        {
            GameFunctions.RequestOfficialRace(app, context, resetPlayer, custom);
        }
        finally
        {
            insideOfficialRequest = false;
        }
        try
        {
            if (automatic && *context == -1)
            {
                // Completion includes denial/failure. Success is the official flag, set by
                // RequestOfficialRace after PayRequest, together with the custom mode at +0x26c.
                var official = Memory.ReadField<int>(app, 0x268) != 0;

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
                // The same choice as DialogConfirmOfficial_OnOk.
                // Skip only the allowed confirmation UI, the original request still authorizes and pays for the run
                *(int*)((byte*)menus + 0x700) = 0x1f;
                return;
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }

        GameFunctions.ConfirmOfficial(menus, allowed, cost, balance, title, message);
    }

    private static void TranslateRetryChoice(nint menus)
    {
        var choice = Memory.ReadField<int>(menus, 0x700);
        var next = session.ResultChoice(choice);
        if (choice != next)
        {
            *(int*)((byte*)menus + 0x700) = next;
        }
    }

    private static void ResetSession()
    {
        session.Reset();
        RestartInputs.Reset();
    }

    private static void Fail(Exception e)
    {
        failed = true;
        ResetSession();

        try
        {
            Log.Error("Official hooks disabled for this process", e);
        }
        catch { }
    }
}
