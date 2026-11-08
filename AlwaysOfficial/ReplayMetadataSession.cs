namespace AlwaysOfficial;

internal sealed class ReplayMetadataSession
{
    private sealed class RunMetadata
    {
        public byte Carried;
        public long StartedAt;
        public long StoppedAt;
        public bool Official;
    }

    private readonly Dictionary<nint, RunMetadata> ghosts = [];
    private RunMetadata run = new();
    private bool retry;

    public void BeginRetry()
    {
        EndRun();
        retry = true;
    }

    public void EndRun()
    {
        run = new();
        retry = false;
    }

    public void Carried(int button)
    {
        if (retry)
        {
            run.Carried |= (byte)(1 << button);
        }
    }

    public void StartRecord(nint ghost)
    {
        // Replacement ghosts share the same Go/finish timestamps and input provenance.
        ghosts[ghost] = run;
    }

    public void Start(long timestamp)
    {
        if (run.StartedAt != 0)
        {
            // Another Go without EndRun is a new attempt like Restart -> ResetRaceLocal
            // OnStartLine restarts the replay after this transition, so its new ghost gets the fresh run
            EndRun();
        }

        run.StartedAt = timestamp;
    }

    public void Stop(long timestamp)
    {
        if (run.StartedAt != 0 && run.StoppedAt == 0)
        {
            run.StoppedAt = timestamp;
        }
    }

    public void StartOfficial() => run.Official = true;
    public void Destroy(nint ghost) => ghosts.Remove(ghost);

    public int Encode(nint ghost, Span<byte> destination, Version version, ReadOnlySpan<byte> gitCommit)
    {
        return ghosts.TryGetValue(ghost, out var metadata)
            ? ReplayMetadataChunk.Encode(destination, version, metadata.Carried,
                gitCommit, metadata.StartedAt, metadata.StoppedAt, metadata.Official)
            : 0;
    }
}
