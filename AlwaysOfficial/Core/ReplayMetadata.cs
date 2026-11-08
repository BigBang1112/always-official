using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AlwaysOfficial.Core;

internal static unsafe class ReplayMetadata
{
    private static readonly object gate = new();

    private static readonly ReplayMetadataSession session = new();
    internal static nint ArchiveThunk { get; private set; }

    internal static void Initialize(nint module)
    {
        ArchiveThunk = Win32.GetProcAddress(module, "ModArchiveEndThunk");

        if (ArchiveThunk == 0)
        {
            throw new InvalidOperationException("Archive hook thunk is missing");
        }
    }

    internal static void BeginRetry()
    {
        lock (gate)
        {
            session.BeginRetry();
        }
    }

    internal static void EndRun()
    {
        lock (gate)
        {
            session.EndRun();
        }
    }

    // Only substitutions of a remembered press for a zero sample count as carried input
    internal static void Carried(int button)
    {
        lock (gate)
        {
            session.Carried(button);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    internal static void StartRecordDetour(nint ghost, nint mobil, uint time)
    {
        try
        {
            lock (gate)
            {
                session.StartRecord(ghost);
            }
        }
        catch { } // Metadata must not prevent the game from starting a recording

        GameFunctions.GhostStartRecord(ghost, mobil, time);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    internal static void StartOfficialDetour(nint app)
    {
        try
        {
            lock (gate)
            {
                session.StartOfficial();
            }
        }
        catch { } // Metadata must not interrupt official recording

        GameFunctions.StartOfficialRecord(app);
    }

    // These CALL sites are the Go transition and the accepted final finish
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    internal static void RaceStateDetour(nint player, uint time, int state)
    {
        try
        {
            // Check the transition first, it only needs the player's current state at +0x314
            var previous = Memory.ReadField<int>(player, 0x314);
            var go = previous == 0 && state == 1;
            var finish = previous == 1 && state == 2;

            if (go || finish)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var race = OfficialHooks.CurrentRace;

                if (race != 0 && player == GameFunctions.GetLocalPlayerInfo(race))
                {
                    lock (gate)
                    {
                        if (go)
                        {
                            session.Start(timestamp);
                        }
                        else
                        {
                            session.Stop(timestamp);
                        }
                    }
                }
            }
        }
        catch { } // Metadata must not interrupt a gameplay transition

        GameFunctions.ChangeRaceState(player, time, state);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    internal static void DestroyDetour(nint ghost)
    {
        try
        {
            lock (gate)
            {
                session.Destroy(ghost);
            }
        }
        catch { }

        GameFunctions.GhostDestroy(ghost);
    }

    // The native thunk supplies EDI (the node), before WriteMask's original stack arguments
    internal static void ArchiveEnd(nint archive, nint node, uint* values, uint count)
    {
        Span<byte> chunk = stackalloc byte[ReplayMetadataChunk.MaxSize];

        var size = 0;

        try
        {
            if (count == 1 && *values == 0xFACADE01 &&
                Memory.ReadField<int>(archive, 8) != 0 && Memory.ReadField<int>(archive, 12) == 0)
            {
                lock (gate)
                {
                    size = session.Encode(node, chunk, ModInfo.LibraryVersion, ModInfo.GitCommit);
                }
            }
        }
        catch { }

        if (size != 0)
        {
            fixed (byte* bytes = chunk)
            {
                GameFunctions.WriteArchiveData(archive, bytes, (uint)size);
            }
        }

        GameFunctions.WriteArchiveMask(archive, values, count);
    }
}
