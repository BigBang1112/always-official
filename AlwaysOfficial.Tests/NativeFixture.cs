using System.Reflection;
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
    private static nint recordedGhost;
    private static readonly List<byte> archiveBytes = [];
    private static int ghostStarts;
    private static int ghostDestructions;
    private static int officialStarts;
    private static int officialStops;
    private static long officialBefore;
    private static long officialAfter;
    private static readonly int[] cachedValues = new int[7];
    private static readonly List<(nint Action, int Value, uint Time)> inputEvents = [];
    private static readonly (uint Site, uint Target)[] calls =
    [
        (0x49cdaf, 0x491080), (0x491bc8, 0x47af80), (0x49142e, 0x4d0270),
        (0x4914fb, 0x4d0270), (0x4915d3, 0x490e00), (0x491e8b, 0x490e00),
        (0x4927c6, 0x490e00), (0x48b2ba, 0x62da90),
        (0x8f914a, 0x8f6f50), (0x8fc266, 0x8f6fc0),
        (0x5dd61f, 0x5dfe90), (0x5e0cd3, 0x5e0250), (0x9242f2, 0x909e60),
        (0x47dc30, 0x4bbb70), (0x480e7f, 0x4bcc70),
    ];

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint VirtualAlloc(nint address, nuint size, uint allocationType, uint protection);

    [LibraryImport("kernel32.dll")]
    private static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlushInstructionCache(nint process, nint address, nuint size);

    private static string NativeTestDll => typeof(NativeFixture).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == nameof(NativeTestDll)).Value!;

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
        if (scenario == nameof(NativeTests.StandaloneProxyLoadsAdjacentModAndForwards))
        {
            var proxy = NativeLibrary.Load(Path.Combine(root, "dist", "d3d9.dll"));
            Check(NativeLibrary.TryGetExport(proxy, "AlwaysOfficialStandaloneProxy", out _), "Proxy marker is missing");
            Check(!NativeLibrary.TryGetExport(proxy, "ModBoot", out _), "The proxy must not contain the mod runtime");
            var create9 = (delegate* unmanaged[Stdcall]<uint, nint>)NativeLibrary.GetExport(proxy, "Direct3DCreate9");
            var instance = create9(32);
            Check(instance != 0, "Standalone Direct3D forwarding failed");
            ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)instance)[2])(instance);
            var mod = Win32.GetModuleHandle("AlwaysOfficial.dll");
            Check(mod != 0 && NativeLibrary.TryGetExport(mod, "ModBoot", out _), "Proxy did not load the adjacent mod");
            // ModBoot creates this mutex; the proxy must have waited for it before Direct3DCreate9 returned.
            var mutex = Win32.CreateMutex(0, false, $"Local\\AlwaysOfficial_{Win32.GetCurrentProcessId()}");
            var booted = Marshal.GetLastPInvokeError() == Win32.ERROR_ALREADY_EXISTS;
            Win32.CloseHandle(mutex);
            Check(booted, "Direct3DCreate9 returned before the mod booted");
            return;
        }
        var production = scenario == nameof(NativeTests.DistributedDllLoadsAndForwards);
        var path = production ? Path.Combine(root, "dist", "AlwaysOfficial.dll") : NativeTestDll;
        Check(File.Exists(path), $"Missing {path}. Build the tests without -p:PublishModForTests=false, or publish first: dotnet publish AlwaysOfficial -c <test configuration>, and again with -p:NativeTestBuild=true");
        // Keep the DLL loaded until worker exit; Native AOT does not support unloading.
        var dll = NativeLibrary.Load(path);
        var testExport = NativeLibrary.TryGetExport(dll, "TestInstall", out var install);
        Check(testExport != production, "Test-only export must be absent from the distributed DLL");
        var create = (delegate* unmanaged[Stdcall]<uint, nint>)NativeLibrary.GetExport(dll, "Direct3DCreate9");
        Check(NativeLibrary.GetExport(dll, "ModBoot") != 0, "ModBoot export is missing");
        Check(NativeLibrary.TryGetExport(dll, "ModWaitBooted", out _), "ModWaitBooted export is missing (the standalone proxy needs it)");
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
        Branch(0x5dfe90, (nint)(delegate* unmanaged[Thiscall]<nint, nint, uint, void>)&FakeGhostStart, 0xe9);
        Branch(0x5e0250, (nint)(delegate* unmanaged[Thiscall]<nint, void>)&FakeGhostDestroy, 0xe9);
        Branch(0x909e60, (nint)(delegate* unmanaged[Thiscall]<nint, uint*, uint, void>)&FakeWriteMask, 0xe9);
        Branch(0x909a60, (nint)(delegate* unmanaged[Thiscall]<nint, byte*, uint, void>)&FakeWriteData, 0xe9);
        Branch(0x4bbb70, (nint)(delegate* unmanaged[Thiscall]<nint, void>)&FakeOfficialStart, 0xe9);
        Branch(0x4bcc70, (nint)(delegate* unmanaged[Thiscall]<nint, nint, void>)&FakeOfficialStop, 0xe9);
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

        if (scenario == nameof(NativeTests.GhostMetadataSurvivesLaterRunsAndSkipsUnrelatedNodes))
        {
            ExerciseMetadata();
        }
        else if (scenario == nameof(NativeTests.CountdownReleaseWithoutBufferedKeyUp))
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

    private static void ExerciseMetadata()
    {
        CreateGame();
        var run = (delegate* unmanaged[Thiscall]<nint, nint*, int*, int, void>)Target(0x491bc8);
        var request = (delegate* unmanaged[Thiscall]<nint, nint*, int, int, void>)Target(0x491e8b);
        nint context = 1;
        var status = 0;
        request(currentApp, &context, 0, 0);
        var manualGhost = recordedGhost;
        EmitButton(0, true);
        EmitButton(1, true);
        raceStatus = 3;
        run(gameRace, &context, &status, 0);
        request(currentApp, &context, 0, 0);
        var retryGhost = recordedGhost;
        officialBefore = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ((delegate* unmanaged[Thiscall]<nint, void>)Target(0x47dc30))(currentApp);
        // The game may replace a recording during the same reset. Preserve the run's
        // provenance even when the reset samples preceded creation of its final ghost.
        var replacementGhost = (nint)NativeMemory.AllocZeroed(0x198);
        ((delegate* unmanaged[Thiscall]<nint, nint, uint, void>)Target(0x5dd61f))(replacementGhost, 1, 0);
        EmitButton(0, false);
        EmitButton(1, false);
        ((delegate* unmanaged[Thiscall]<nint, nint, void>)Target(0x480e7f))(currentApp, gameRace);
        officialAfter = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Check(officialStarts == 1 && officialStops == 1, "Official callbacks did not forward their arguments");
        raceStatus = 0;
        run(gameRace, &context, &status, 0);
        context = 0;
        request(currentApp, &context, 0, 0); // A later run must not change the saved ghost's flags.
        Check(ghostStarts == 4, "Ghost start forwarding failed");

        // Exercise the actual patched CALL and native EDI-capturing thunk. This helper has
        // thiscall arguments (archive, node, values, count), preserves EDI, and pops 12 bytes.
        byte[] helper = [0x57, 0x8b, 0x7c, 0x24, 0x08,
            0xff, 0x74, 0x24, 0x10, 0xff, 0x74, 0x24, 0x10,
            0x90, 0x90, 0x90, 0x90];
        helper.CopyTo(new Span<byte>((void*)At(0x9242e1), helper.Length));
        new byte[] { 0x5f, 0xc2, 0x0c, 0x00 }.CopyTo(new Span<byte>((void*)At(0x9242f7), 4));
        Check(FlushInstructionCache(GetCurrentProcess(), At(0x9242e1), 26), "Cannot flush archive helper");
        var archive = (nint)NativeMemory.AllocZeroed(0x20);
        Field<int>(archive, 8) = 1;
        CheckMetadata(archive, retryGhost, 3, true);
        CheckMetadata(archive, replacementGhost, 3, true);
        CheckMetadata(archive, manualGhost, 0);
        CheckMetadata(archive, recordedGhost, 0);

        CheckArchiveUnchanged(archive, (nint)0x123456); // Untracked or loaded nodes.
        Field<int>(archive, 12) = 1;
        CheckArchiveUnchanged(archive, retryGhost); // Text archive.
        Field<int>(archive, 12) = 0;
        Field<int>(archive, 8) = 0;
        CheckArchiveUnchanged(archive, retryGhost); // Reading.
        Field<int>(archive, 8) = 1;

        ((delegate* unmanaged[Thiscall]<nint, void>)Target(0x5e0cd3))(retryGhost);
        Check(ghostDestructions == 1, "Ghost destructor forwarding failed");
        CheckArchiveUnchanged(archive, retryGhost); // Reused native addresses must not retain old metadata.
        ((delegate* unmanaged[Thiscall]<nint, nint, uint, void>)Target(0x5dd61f))(retryGhost, 1, 0);
        CheckMetadata(archive, retryGhost, 0);
    }

    private static void ArchiveNode(nint archive, nint node)
    {
        archiveBytes.Clear();
        uint end = 0xfacade01;
        ((delegate* unmanaged[Thiscall]<nint, nint, uint*, uint, void>)At(0x9242e1))(archive, node, &end, 1);
    }

    private static void CheckMetadata(nint archive, nint ghost, byte expectedFlags, bool timestamps = false)
    {
        ArchiveNode(archive, ghost);
        using var reader = new BinaryReader(new MemoryStream(archiveBytes.ToArray()));
        Check(reader.ReadUInt32() == 0xBB111201, "Wrong metadata chunk ID");
        Check(reader.ReadUInt32() == 0x534b4950, "Missing skippable marker");
        var length = reader.ReadUInt32();
        var payloadEnd = reader.BaseStream.Position + length;
        Check(reader.ReadByte() == 0, "Wrong metadata format version");
        var flags = reader.ReadByte();
        Check((flags & 3) == expectedFlags && (flags & 0xc0) == 0, "Carried-input provenance changed");
        Check((flags & 0x30) == (timestamps ? 0x30 : 0), "Timestamp presence changed");
        var libraryVersion = System.Diagnostics.FileVersionInfo.GetVersionInfo(NativeTestDll);
        Check(reader.Read7BitEncodedInt() == libraryVersion.FileMajorPart, "Wrong major version");
        Check(reader.Read7BitEncodedInt() == libraryVersion.FileMinorPart, "Wrong minor version");
        Check(reader.Read7BitEncodedInt() == libraryVersion.FileBuildPart, "Wrong build version");
        var revision = (flags & 4) != 0 ? reader.Read7BitEncodedInt() : 0;
        Check(revision == libraryVersion.FilePrivatePart, "Wrong revision");
        var productVersion = libraryVersion.ProductVersion ?? "";
        var commit = productVersion.Contains('+') ? productVersion.Split('+')[1] : "";
        var expectedCommit = commit.Length == 40 ? commit : new string('0', 40);
        Check(Convert.ToHexString(reader.ReadBytes(20)).Equals(expectedCommit, StringComparison.OrdinalIgnoreCase),
            "Wrong Git commit");
        var runStartedAt = reader.Read7BitEncodedInt64();
        var runEndedAt = (flags & 8) != 0 ? reader.Read7BitEncodedInt64() : 0;
        Check(runStartedAt > 0 && (runEndedAt == 0 || runEndedAt >= runStartedAt), "Wrong run timestamps");
        if (timestamps)
        {
            var startedAt = reader.Read7BitEncodedInt64();
            var stoppedAt = reader.Read7BitEncodedInt64();
            Check(startedAt >= officialBefore && startedAt <= officialAfter &&
                stoppedAt >= startedAt && stoppedAt <= officialAfter, "Wrong official-call timestamps");
        }
        Check(reader.BaseStream.Position == payloadEnd, "Incorrect payload length");
        // An unknown-chunk reader skips exactly this length and must land on the node terminator.
        reader.BaseStream.Position = payloadEnd;
        Check(reader.ReadUInt32() == 0xfacade01 && reader.BaseStream.Position == reader.BaseStream.Length,
            "Metadata corrupted or duplicated the node terminator");
    }

    private static void CheckArchiveUnchanged(nint archive, nint node)
    {
        ArchiveNode(archive, node);
        Check(archiveBytes.SequenceEqual(new byte[] { 1, 0xde, 0xca, 0xfa }), "Unrelated archive was modified");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeGhostStart(nint ghost, nint mobil, uint time)
    {
        ghostStarts++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeOfficialStart(nint app)
    {
        if (app == currentApp)
        {
            officialStarts++;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeOfficialStop(nint app, nint player)
    {
        if (app == currentApp && player == gameRace)
        {
            officialStops++;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeGhostDestroy(nint ghost)
    {
        ghostDestructions++;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeWriteMask(nint archive, uint* values, uint count)
    {
        archiveBytes.AddRange(new ReadOnlySpan<byte>(values, checked((int)count * 4)).ToArray());
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    private static void FakeWriteData(nint archive, byte* bytes, uint count)
    {
        archiveBytes.AddRange(new ReadOnlySpan<byte>(bytes, checked((int)count)).ToArray());
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

        recordedGhost = (nint)NativeMemory.AllocZeroed(0x198);
        ((delegate* unmanaged[Thiscall]<nint, nint, uint, void>)Target(0x5dd61f))(recordedGhost, 1, 0);
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

