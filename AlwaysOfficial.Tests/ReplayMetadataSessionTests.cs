namespace AlwaysOfficial.Tests;

public class ReplayMetadataSessionTests
{
    [Test]
    public async Task TimestampsCaptureOnlyGoAndFinish()
    {
        var session = new ReplayMetadataSession();
        session.StartRecord(1);
        await Assert.That(ReadRunTimes(Encode(session, 1))).IsEqualTo((0L, 0L));
        session.Start(100);
        await Assert.That(ReadRunTimes(Encode(session, 1))).IsEqualTo((100L, 0L));
        session.Stop(200);
        session.Stop(250);
        session.EndRun();
        await Assert.That(ReadRunTimes(Encode(session, 1))).IsEqualTo((100L, 200L));
        session.StartRecord(2);
        session.Start(300);
        session.BeginRetry();
        await Assert.That(ReadRunTimes(Encode(session, 2))).IsEqualTo((300L, 0L));
        session.StartRecord(3);
        session.Stop(400);
        session.EndRun();
        await Assert.That(ReadRunTimes(Encode(session, 3))).IsEqualTo((0L, 0L));
    }

    private static (long Start, long End) ReadRunTimes(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position = 37; // Framing, format, flags, 0.1.0 version and commit.
        return (reader.Read7BitEncodedInt64(), (bytes[13] & 8) != 0 ? reader.Read7BitEncodedInt64() : 0);
    }

    [Test]
    public async Task RestartWithoutEndRunGetsItsOwnTimestamps()
    {
        // Pause menu Restart resets the race without a Run status that ends the session,
        // OnStartLine then reports Go before restarting the replay recording.
        var session = new ReplayMetadataSession();
        session.Start(100);
        session.StartRecord(1);
        session.Start(300);
        session.StartRecord(2);
        session.Stop(400);
        session.EndRun();
        await Assert.That(ReadRunTimes(Encode(session, 1))).IsEqualTo((100L, 0L));
        await Assert.That(ReadRunTimes(Encode(session, 2))).IsEqualTo((300L, 400L));
    }

    [Test]
    public async Task ReplacementGhostSharesRunButLaterRetryDoesNotChangeSavedMetadata()
    {
        var session = new ReplayMetadataSession();
        session.BeginRetry();
        session.Carried(0);
        session.StartRecord(1);
        session.Start(100);
        session.StartOfficial();
        session.StartRecord(2);
        session.Carried(1);
        session.Stop(200);
        session.EndRun();
        var first = Encode(session, 1);
        await Assert.That(Convert.ToHexString(Encode(session, 2))).IsEqualTo(Convert.ToHexString(first));
        await Assert.That(first[13]).IsEqualTo((byte)27);

        session.BeginRetry();
        session.StartRecord(3);
        await Assert.That(Encode(session, 3)[13]).IsEqualTo((byte)0);
        session.Start(100);
        session.StartOfficial();
        await Assert.That(Convert.ToHexString(Encode(session, 1))).IsEqualTo(Convert.ToHexString(first));
        await Assert.That(Encode(session, 3)[13]).IsEqualTo((byte)16);
    }

    [Test]
    public async Task DestroyedGhostLosesMetadataAndReusedAddressStartsFresh()
    {
        var session = new ReplayMetadataSession();
        session.BeginRetry();
        session.StartRecord(1);
        session.Carried(0);
        session.Destroy(1);
        await Assert.That(Encode(session, 1).Length).IsEqualTo(0);

        session.EndRun();
        session.Carried(1);
        session.StartRecord(1);
        await Assert.That(Encode(session, 1)[13]).IsEqualTo((byte)0);
    }

    private static byte[] Encode(ReplayMetadataSession session, nint ghost)
    {
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = session.Encode(ghost, bytes, new Version(0, 1, 0, 0), new byte[20]);
        return bytes[..size];
    }
}
