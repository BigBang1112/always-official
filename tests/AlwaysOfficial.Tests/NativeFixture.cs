using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AlwaysOfficial.Core;

namespace AlwaysOfficial.Tests;

internal static unsafe partial class NativeFixture
{
    private static nint imageBase;
    private static nint currentApp;
    private static nint gameRace;
    private static nint gameMenus;
    private static int raceStatus;
    private static int confirmationCalls;
    private static int cancellationCalls;
    private static int requestedCustom;
    private static bool requestAllowed = true;
    private static bool endChallenge;
    private static bool requestPending;
    private static bool physicalAccelerate;
    private static bool physicalBrake;
    private static nint inputPort;
    private static nint stateEvent;
    private static readonly int[] cachedValues = new int[7];
    private static readonly List<(nint Action, int Value, uint Time)> inputEvents = [];
    private static readonly (uint Site, uint Target)[] calls =
    [
        (0x49cdaf, 0x491080), (0x491bc8, 0x47af80), (0x49142e, 0x4d0270),
        (0x4914fb, 0x4d0270), (0x4915d3, 0x490e00), (0x491e8b, 0x490e00),
        (0x4927c6, 0x490e00), (0x48b2ba, 0x62da90),
        (0x8f914a, 0x8f6f50), (0x8fc266, 0x8f6fc0),
    ];

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushInstructionCache(nint process, nint address, nuint size);

    private static nint At(uint address)
    {
        return imageBase + (int)(address - 0x400000);
    }

    private static ref T Field<T>(nint obj, int offset) where T : unmanaged
    {
        return ref *(T*)(obj + offset);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static nint Target(uint site)
    {
        return At(site) + 5 + *(int*)(At(site) + 1);
    }

    private static void Branch(uint site, nint target, byte opcode = 0xe8)
    {
        var code = CallPatch.Encode((uint)At(site), (uint)target);
        code[0] = opcode;
        code.CopyTo(new Span<byte>((void*)At(site), 5));
    }

    public static void Run(string scenario)
    {
        Check(IntPtr.Size == 4, "Native checks require an x86 TUnit process");
        var root = NativeTests.FindRoot();
        var production = scenario == nameof(NativeTests.DistributedDllLoadsAndForwards);
        var path = Path.Combine(root, production ? "dist" : "build/native-tests", "AlwaysOfficial.dll");
        Check(File.Exists(path), $"Missing {path}. Run build.ps1 -Tests first.");
        // Keep the DLL loaded until worker exit; Native AOT does not support unloading.
        var dll = NativeLibrary.Load(path);
        var testExport = NativeLibrary.TryGetExport(dll, "TestInstall", out var install);
        Check(testExport != production, "Test-only export must be absent from the distributed DLL");
        var create = (delegate* unmanaged[Stdcall]<uint, nint>)NativeLibrary.GetExport(dll, "Direct3DCreate9");
        Check(NativeLibrary.GetExport(dll, "ModBoot") != 0, "ModBoot export is missing");
        var d3d = create(32); // D3D_SDK_VERSION; synchronizes with the shim's boot thread.
        Check(d3d != 0, "Direct3DCreate9 forwarding failed");
        var release = (delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)d3d)[2];
        release(d3d);
        var perf = (delegate* unmanaged[Stdcall]<uint>)NativeLibrary.GetExport(dll, "D3DPERF_GetStatus");
        _ = perf(); // Exercise a naked forwarding stub as well as the typed export.
        if (production)
        {
            return;
        }

        imageBase = VirtualAlloc(0, 0xa00000, 0x3000, 0x40);
        Check(imageBase != 0, "Cannot allocate synthetic image");
        new Span<byte>((void*)imageBase, 0xa00000).Fill(0xcc);
        foreach (var call in calls)
        {
            Branch(call.Site, At(call.Target));
        }

        Branch(0x47af80, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)&FakeRun, 0xe9);
        Branch(0x4d0270, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, void>)&FakeResult, 0xe9);
        Branch(0x490e00, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)&FakeRequest, 0xe9);
        Branch(0x62da90, (nint)(delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void>)&FakeConfirm, 0xe9);
        Branch(0x4bcc60, (nint)(delegate* unmanaged[Thiscall]<nint, void>)&FakeCancel, 0xe9);
        Branch(0x491080, (nint)(delegate* unmanaged[Thiscall]<nint, nint*, void>)&FakeChallenge, 0xe9);
        Branch(0x8f6f50, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, int>)&FakeSetState, 0xe9);
        Branch(0x8f8160, (nint)(delegate* unmanaged[Thiscall]<nint, nint, nint>)&FakeGetActionState, 0xe9);
        Branch(0x8f6fc0, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, int>)&FakeButtonEvent, 0xe9);
        for (var i = 0; i < 6; i++)
        {
            Field<nint>(At(0xcd3f64), i * 4) = (nint)NativeMemory.AllocZeroed(0x14);
        }

        var mismatch = scenario == nameof(NativeTests.MismatchedImageLeavesEveryCallUntouched);

        if (mismatch)
        {
            *(byte*)At(0x48b2ba) = 0x90;
        }

        var original = calls.Select(c => new ReadOnlySpan<byte>((void*)At(c.Site), 5).ToArray()).ToArray();
        Check(FlushInstructionCache(GetCurrentProcess(), imageBase, 0xa00000), "Cannot flush synthetic code");
        var installed = ((delegate* unmanaged[Stdcall]<nint, nint, int>)install)(imageBase,
            (nint)(delegate* unmanaged[Stdcall]<int, short>)&FakeKeyState);
        Check(installed == (mismatch ? 0 : 1), "Unexpected hook installation result");
        if (mismatch)
        {
            for (var i = 0; i < calls.Length; i++)
            {
                Check(new ReadOnlySpan<byte>((void*)At(calls[i].Site), 5).SequenceEqual(original[i]), "Mismatched image was modified");
            }

            return;
        }
        foreach (var call in calls)
        {
            Check(Target(call.Site) != At(call.Target), "Verified CALL was not hooked");
        }

        if (scenario == nameof(NativeTests.CountdownReleaseWithoutBufferedKeyUp))
        {
            ExerciseCountdownRelease();
        }
        else if (scenario == nameof(NativeTests.HeldDrivingInputsSurviveOfficialRestart))
        {
            ExerciseHeldInputs();
        }
        else
        {
            ExerciseCallbacks();
        }
    }

    private static void ExerciseCallbacks()
    {
        CreateGame();
        var run = (delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)Target(0x491bc8);
        var request = (delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)Target(0x491e8b);
        var result = (delegate* unmanaged[Thiscall]<nint, nint*, int, void>)Target(0x4914fb);
        var challenge = (delegate* unmanaged[Thiscall]<nint, nint*, void>)Target(0x49cdaf);
        var fiber = stackalloc int[4];
        new Span<int>(fiber, 4).Clear();
        var context = (nint)fiber;
        var status = 0;
        raceStatus = 3;
        run(gameRace, &context, &status, 0);
        Check(status == 3, "Practice restart changed");
        request(currentApp, &context, 0, 0);
        Check(confirmationCalls == 1, "First manual confirmation was bypassed");
        context = (nint)fiber;
        run(gameRace, &context, &status, 0);
        Check(status == 13 && cancellationCalls == 1, "Official reset or cancellation failed");
        request(currentApp, &context, 0, 0);
        Check(confirmationCalls == 1 && Field<int>(gameMenus, 0x700) == 0x1f, "Automatic confirmation was not answered");
        context = (nint)fiber;
        result(gameMenus, &context, 0);
        Check(Field<int>(gameMenus, 0x700) == 0x6d, "Result Retry did not request official mode");
        request(currentApp, &context, 0, 0);
        context = (nint)fiber;
        fiber[1] = 0x1927;
        Field<int>(gameMenus, 0x700) = 0x1f;
        challenge(currentApp, &context);
        Check(Field<int>(gameMenus, 0x700) == 0x6d, "Pause Retry did not request official mode");
        request(currentApp, &context, 0, 0);
        context = (nint)fiber;
        Field<int>(currentApp, 0x26c) = 1;
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0);
        Check(requestedCustom == 1, "Custom official mode was lost");
        context = (nint)fiber;
        run(gameRace, &context, &status, 0);
        requestAllowed = false;
        var before = confirmationCalls;
        request(currentApp, &context, 0, 0);
        Check(confirmationCalls == before + 1, "Denied request skipped original UI");
        context = (nint)fiber;
        run(gameRace, &context, &status, 0);
        Check(status == 3, "Failed request kept automatic retries armed");
        Field<int>(currentApp, 0x268) = 1;
        raceStatus = 0;
        run(gameRace, &context, &status, 0);
        endChallenge = true;
        challenge(currentApp, &context);
        Field<int>(currentApp, 0x268) = 0;
        context = (nint)fiber;
        raceStatus = 3;
        run(gameRace, &context, &status, 0);
        Check(status == 3, "Challenge exit did not clear activation");
    }

    private static void CreateGame()
    {
        currentApp = (nint)NativeMemory.AllocZeroed(0x600);
        gameRace = (nint)NativeMemory.AllocZeroed(0x600);
        gameMenus = (nint)NativeMemory.AllocZeroed(0x1000);
        Field<nint>(gameRace, 0x18) = currentApp;
        Field<nint>(currentApp, 0x454) = gameRace;
        Field<nint>(currentApp, 0x194) = gameMenus;
        Field<nint>(currentApp, 0x198) = (nint)NativeMemory.AllocZeroed(4);
        inputPort = (nint)NativeMemory.AllocZeroed(0x100);
        Field<nint>(gameRace, 0x20) = inputPort;
        Field<nint>(gameRace, 0xc8) = CreateInputMap(DrivingAction(0), 10, DrivingAction(1), 11);
        Field<nint>(inputPort, 0x20) = Field<nint>(gameRace, 0xc8);
        stateEvent = (nint)NativeMemory.AllocZeroed(8);
        Field<int>(inputPort, 0x90) = 1; // The game input port has focus.
        var device = (nint)NativeMemory.AllocZeroed(0x60);
        var vtable = (nint)NativeMemory.AllocZeroed(0xa0);
        Field<nint>(vtable, 0x78) = (nint)(delegate* unmanaged[Thiscall]<nint, int>)&FakeKeyboardKind;
        Field<nint>(device, 0) = vtable;
        var devices = (nint)NativeMemory.AllocZeroed(4);
        Field<nint>(devices, 0) = device;
        Field<int>(inputPort, 0x2c) = 1;
        Field<nint>(inputPort, 0x30) = devices;
        Field<uint>(At(0xd219e0), 10 * 0x20c) = 0xc8; // Extended DIK_UP.
        Field<uint>(At(0xd219e0), 11 * 0x20c) = 0xd0; // Extended DIK_DOWN.
        var core = (nint)NativeMemory.AllocZeroed(0x100);
        Field<nint>(At(0xd731e0), 0) = core;
        Field<uint>(core, 0xbc) = 100; // Inline timer at +0xa0, tick at +0x1c.
    }

    private static void ExerciseCountdownRelease()
    {
        CreateGame();
        var run = (delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)Target(0x491bc8);
        var request = (delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)Target(0x491e8b);
        nint context = 1;
        var status = 0;
        request(currentApp, &context, 0, 0);
        EmitButton(0, true);
        EmitButton(1, true);
        raceStatus = 3;
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0);
        Check(cachedValues[0] == 1 && cachedValues[1] == 1, "held buttons did not reach countdown");
        physicalAccelerate = false; // OS reports key-up; no DirectInput buffered event exists.
        raceStatus = 0;
        run(gameRace, &context, &status, 0); // Poll while waiting for countdown; no sampler call either.
        Check(cachedValues[0] == 0 && cachedValues[1] == 1, "Countdown release was stuck or released both buttons");
        physicalBrake = false;
        run(gameRace, &context, &status, 0);
        Check(cachedValues[1] == 0, "Countdown brake release was stuck");
        SampleInputs(clear: false, 200);
        Check(cachedValues[0] == 0 && cachedValues[1] == 0, "Post-countdown sampling resurrected released keys");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static int FakeKeyboardKind(nint device)
    {
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static short FakeKeyState(int key)
    {
        return (key == 0x26 && physicalAccelerate) || (key == 0x28 && physicalBrake) ? unchecked((short)0x8000) : (short)0;
    }

    private static void ExerciseHeldInputs()
    {
        CreateGame();
        var run = (delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)Target(0x491bc8);
        var request = (delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)Target(0x491e8b);
        nint context = 1;
        var status = 0;
        EmitButton(0, true);
        request(currentApp, &context, 0, 0); // Manual start does not remember any input.
        Check(cachedValues[0] == 0, "Manual request preserved a button unexpectedly");

        EmitButton(0, true);
        EmitButton(1, true);
        EmitButton(6, true); // Delete must never be carried.
        raceStatus = 3;
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0); // Performs two reset seeds, both reporting all zeros.
        Check(cachedValues[0] == 1 && cachedValues[1] == 1, "Reset seeds lost held accelerator/brake");
        Check(cachedValues[6] == 0, "Delete was remembered");
        for (uint tick = 1; tick <= 120; tick++)
        {
            SampleInputs(clear: false, tick);
            Check(cachedValues[0] == 1 && cachedValues[1] == 1, "Remembered buttons lasted only one frame");
        }
        EmitButton(0, false);
        SampleInputs(clear: false, 121);
        Check(cachedValues[0] == 0 && cachedValues[1] == 1, "Releasing accelerator also lost held brake");
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0); // Delete again without releasing brake.
        Check(cachedValues[0] == 0 && cachedValues[1] == 1, "Second official restart forgot held brake");
        EmitButton(1, false);
        SampleInputs(clear: false, 122);
        Check(cachedValues[1] == 0, "Released brake became stuck");

        EmitButton(0, true);
        run(gameRace, &context, &status, 0);
        requestPending = true;
        request(currentApp, &context, 0, 0);
        var menuAction = (nint)NativeMemory.AllocZeroed(0x14);
        Field<nint>(inputPort, 0x20) = CreateInputMap(menuAction, 10, (nint)0x123456, 99);
        // The same physical key-up is now a menu action while the request is suspended.
        physicalAccelerate = false;
        EmitAction(menuAction, 0);
        Field<nint>(inputPort, 0x20) = Field<nint>(gameRace, 0xc8);
        requestPending = false;
        request(currentApp, &context, 0, 0);
        Check(cachedValues[0] == 0, "Release during pending request was forgotten");

        EmitButton(0, true);
        run(gameRace, &context, &status, 0);
        requestAllowed = false;
        request(currentApp, &context, 0, 0);
        SampleInputs(clear: true, 0);
        Check(cachedValues[0] == 0, "Denied request left remembered input active");

        requestAllowed = true;
        request(currentApp, &context, 0, 0); // Enter official manually again.
        raceStatus = 0;
        run(gameRace, &context, &status, 0);
        EmitButton(0, true);
        raceStatus = 3;
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0);
        Field<int>(inputPort, 0x90) = 0;
        SampleInputs(clear: true, 0);
        Check(cachedValues[0] == 0, "Focus loss left remembered input active");
        Field<int>(inputPort, 0x90) = 1;
        SampleInputs(clear: false, 1);
        Check(cachedValues[0] == 0, "Returning focus resurrected a remembered key");

        EmitButton(1, true);
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0);
        Field<nint>(currentApp, 0x198) = (nint)NativeMemory.AllocZeroed(4);
        raceStatus = 0;
        run(gameRace, &context, &status, 0);
        SampleInputs(clear: true, 0);
        Check(cachedValues[1] == 0, "Changing challenge leaked held input");
    }

    private static nint DrivingAction(int index)
    {
        return index == 6 ? (nint)0x123456 : Field<nint>(At(0xcd3f64), index * 4);
    }

    private static void EmitButton(int index, bool pressed)
    {
        if (index == 0)
        {
            physicalAccelerate = pressed;
        }

        if (index == 1)
        {
            physicalBrake = pressed;
        }

        EmitAction(DrivingAction(index), pressed ? 128 : 0);
    }

    private static void EmitAction(nint action, int value)
    {
        var inputEvent = stackalloc nint[2];
        inputEvent[0] = action;
        inputEvent[1] = value;
        ((delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Target(0x8fc266))(inputPort + 0x40, (nint)inputEvent, 100);
    }

    private static nint CreateInputMap(nint firstAction, int firstObject, nint secondAction, int secondObject)
    {
        var map = (nint)NativeMemory.AllocZeroed(0x44);
        var category = (nint)NativeMemory.AllocZeroed(8);
        Field<int>(category, 4) = 2;
        Field<int>(map, 0x20) = 1;
        Field<nint>(map, 0x24) = category;
        Field<int>(map, 0x2c) = 2;
        var bindings = (nint)NativeMemory.AllocZeroed(16);
        Field<nint>(bindings, 0) = firstAction;
        Field<int>(bindings, 4) = firstObject;
        Field<nint>(bindings, 8) = secondAction;
        Field<int>(bindings, 12) = secondObject;
        Field<nint>(map, 0x30) = bindings;
        return map;
    }

    private static void SampleInputs(bool clear, uint time)
    {
        if (clear)
        {
            Array.Clear(cachedValues);
        }

        var inputEvent = stackalloc nint[2];

        for (var i = 0; i < 7; i++)
        {
            inputEvent[0] = DrivingAction(i);
            inputEvent[1] = 0; // Reproduce the real log: sampler reports zero despite a held button.
            ((delegate* unmanaged[Thiscall]<nint, nint, uint, int>)Target(0x8f914a))(inputPort + 0x40, (nint)inputEvent, time);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static nint FakeGetActionState(nint port, nint action)
    {
        Field<nint>(stateEvent, 0) = action;
        Field<int>(stateEvent, 4) = 0;
        for (var i = 0; i < 7; i++)
        {
            if (action == DrivingAction(i))
            {
                Field<int>(stateEvent, 4) = cachedValues[i];
            }
        }

        return stateEvent;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static int FakeSetState(nint store, nint inputEvent, uint time)
    {
        return StoreEvent(inputEvent, time);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static int FakeButtonEvent(nint store, nint inputEvent, uint time)
    {
        return StoreEvent(inputEvent, time);
    }

    private static int StoreEvent(nint inputEvent, uint time)
    {
        var action = Field<nint>(inputEvent, 0);

        for (var i = 0; i < 7; i++)
        {
            if (action == DrivingAction(i))
            {
                var value = Field<int>(inputEvent, 4);
                cachedValues[i] = i is 0 or 1 or 3 or 4 or 6 ? (value != 0 ? 1 : 0) : value;
                inputEvents.Add((action, cachedValues[i], time));
            }
        }

        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeRun(nint race, nint* context, int* status, int flags)
    {
        *status = raceStatus;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeResult(nint menus, nint* context, int flags)
    {
        Field<int>(menus, 0x700) = 0x1f;
        *context = -1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeConfirm(nint menus, int allowed, uint cost, uint balance, nint title, nint message)
    {
        confirmationCalls++;
        Field<int>(menus, 0x700) = 0x1f;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeCancel(nint app)
    {
        Field<int>(app, 0x268) = 0;
        cancellationCalls++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeRequest(nint app, nint* context, int resetPlayer, int custom)
    {
        if (requestPending)
        {
            return;
        }

        SampleInputs(clear: true, 0);
        SampleInputs(clear: true, 0); // ResetRaceLocal followed by ResetPlayer.
        inputEvents.Clear();
        requestedCustom = custom;
        ((delegate* unmanaged[Thiscall]<nint, int, uint, uint, nint, nint, void>)Target(0x48b2ba))(gameMenus, requestAllowed ? 1 : 0, 10, 100, 0, 0);
        Field<int>(app, 0x268) = requestAllowed ? 1 : 0;
        Field<int>(app, 0x26c) = custom;
        *context = -1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeChallenge(nint app, nint* context)
    {
        if (endChallenge)
        {
            *context = -1;
        }
    }
}

