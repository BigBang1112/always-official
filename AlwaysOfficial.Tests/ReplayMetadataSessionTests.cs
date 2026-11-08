namespace AlwaysOfficial.Tests;

public class ReplayMetadataSessionTests
{
    [Test]
    public async Task OfflineRunEndsOnceAndNextRecordingGetsItsOwnTimestamps()
    {
        var session = new ReplayMetadataSession();
        session.StartRecord(1, 100);
        await Assert.That(ReadRunTimes(Encode(session, 1))).IsEqualTo((100L, 0L));
        session.StartRecord(2, 200);
        await Assert.That(ReadRunTimes(Encode(session, 1))).IsEqualTo((100L, 200L));
        session.EndRun(300);
        session.EndRun(400);
        await Assert.That(ReadRunTimes(Encode(session, 2))).IsEqualTo((200L, 300L));
        await Assert.That(Encode(session, 2)[13]).IsEqualTo((byte)8);
    }

    private static (long Start, long End) ReadRunTimes(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        reader.BaseStream.Position = 37; // Framing, format, flags, 0.1.0 version and commit.
        return (reader.Read7BitEncodedInt64(), (bytes[13] & 8) != 0 ? reader.Read7BitEncodedInt64() : 0);
    }

    [Test]
    public async Task ReplacementGhostSharesRunButLaterRetryDoesNotChangeSavedMetadata()
    {
        var session = new ReplayMetadataSession();
        session.BeginRetry(10);
        session.Carried(0);
        session.StartRecord(1, 20);
        session.StartOfficial(100);
        session.StartRecord(2, 30);
        session.Carried(1);
        session.StopOfficial(200);
        session.EndRun(250);
        var first = Encode(session, 1);
        await Assert.That(Convert.ToHexString(Encode(session, 2))).IsEqualTo(Convert.ToHexString(first));
        await Assert.That(first[13]).IsEqualTo((byte)59);

        session.BeginRetry(10);
        session.StartRecord(3, 40);
        session.StartOfficial(300);
        await Assert.That(Convert.ToHexString(Encode(session, 1))).IsEqualTo(Convert.ToHexString(first));
        await Assert.That(Encode(session, 3)[13]).IsEqualTo((byte)16);
    }

    [Test]
    public async Task DestroyedGhostLosesMetadataAndReusedAddressStartsFresh()
    {
        var session = new ReplayMetadataSession();
        session.BeginRetry(10);
        session.StartRecord(1, 20);
        session.Carried(0);
        session.Destroy(1);
        await Assert.That(Encode(session, 1).Length).IsEqualTo(0);

        session.EndRun(400);
        session.Carried(1);
        session.StartRecord(1, 20);
        await Assert.That(Encode(session, 1)[13]).IsEqualTo((byte)0);
    }

    private static byte[] Encode(ReplayMetadataSession session, nint ghost)
    {
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = session.Encode(ghost, bytes, new Version(0, 1, 0, 0), new byte[20]);
        return bytes[..size];
    }
}
