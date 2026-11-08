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
            session.BeginRetry(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
    }

    internal static void EndRun()
    {
        lock (gate)
        {
            session.EndRun(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
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
                session.StartRecord(ghost, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            }
        }
        catch { } // Metadata must not prevent the game from starting a recording

        GameFunctions.GhostStartRecord(ghost, mobil, time);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    internal static void StartOfficialDetour(nint app)
    {
        StampOfficial(true);
        GameFunctions.StartOfficialRecord(app);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvThiscall)])]
    internal static void StopOfficialDetour(nint app, nint player)
    {
        StampOfficial(false);
        GameFunctions.StopOfficialRecord(app, player);
    }

    private static void StampOfficial(bool start)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            lock (gate)
            {
                if (start)
                {
                    session.StartOfficial(timestamp);
                }
                else
                {
                    session.StopOfficial(timestamp);
                }
            }
        }
        catch { } // Timestamp collection must not interrupt official recording
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
            if (count == 1 && *values == 0xfacade01 &&
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
