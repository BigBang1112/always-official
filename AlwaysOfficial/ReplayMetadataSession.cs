namespace AlwaysOfficial;

internal sealed class ReplayMetadataSession
{
    private sealed class RunMetadata
    {
        public byte Carried;
        public long RunStartedAt;
        public long RunEndedAt;
        public bool Recording;
        public long? StartedAt;
        public long? StoppedAt;
    }

    private readonly Dictionary<nint, RunMetadata> ghosts = [];
    private RunMetadata run = new();
    private bool pendingRetry;
    private bool retryRecording;

    public void BeginRetry(long timestamp)
    {
        EndRun(timestamp);
        pendingRetry = true;
    }

    public void EndRun(long timestamp)
    {
        if (run.Recording && run.RunEndedAt == 0)
        {
            run.RunEndedAt = timestamp;
        }

        run = new();
        pendingRetry = retryRecording = false;
    }

    public void Carried(int button)
    {
        if (pendingRetry || retryRecording)
        {
            run.Carried |= (byte)(1 << button);
        }
    }

    public void StartRecord(nint ghost, long timestamp)
    {
        if (!pendingRetry && !retryRecording && run.Recording)
        {
            EndRun(timestamp);
        }

        retryRecording |= pendingRetry;

        if (!retryRecording)
        {
            run.Carried = 0;
        }

        pendingRetry = false;

        if (!run.Recording)
        {
            run.RunStartedAt = timestamp;
            run.Recording = true;
        }
        ghosts[ghost] = run;
    }

    public void StartOfficial(long timestamp) => run.StartedAt = timestamp;
    public void StopOfficial(long timestamp) => run.StoppedAt = timestamp;
    public void Destroy(nint ghost) => ghosts.Remove(ghost);

    public int Encode(nint ghost, Span<byte> destination, Version version, ReadOnlySpan<byte> gitCommit)
    {
        return ghosts.TryGetValue(ghost, out var metadata)
            ? ReplayMetadataChunk.Encode(destination, version, metadata.Carried,
                metadata.StartedAt, metadata.StoppedAt, gitCommit, metadata.RunStartedAt, metadata.RunEndedAt)
            : 0;
    }
}
