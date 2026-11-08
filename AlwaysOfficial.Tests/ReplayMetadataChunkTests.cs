using System.Buffers.Binary;

namespace AlwaysOfficial.Tests;

public class ReplayMetadataChunkTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RunEndPresencePrecedesOfficialTimestamps(bool ended)
    {
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = ReplayMetadataChunk.Encode(bytes, new Version(0, 1, 0, 0), 0,
            300, 400, runStartedAt: 100, runEndedAt: ended ? 200 : 0);
        await Assert.That(bytes[13]).IsEqualTo((byte)(48 | (ended ? 8 : 0)));
        using var reader = new BinaryReader(new MemoryStream(bytes, 0, size));
        reader.BaseStream.Position = 37;
        await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(100L);
        if (ended)
        {
            await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(200L);
        }
        await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(300L);
        await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(400L);
        await Assert.That(reader.BaseStream.Position).IsEqualTo(reader.BaseStream.Length);
    }

    [Test]
    public async Task FullCommitUsesTwentyBytesAndFitsMaximumPayload()
    {
        const string hash = "0123456789abcdef0123456789abcdef01234567";
        var commit = Convert.FromHexString(hash);
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = ReplayMetadataChunk.Encode(bytes, new Version(65535, 65535, 65535, 65535), 3,
            long.MaxValue, long.MaxValue, commit, long.MaxValue, long.MaxValue);
        await Assert.That(size).IsEqualTo(ReplayMetadataChunk.MaxSize);
        await Assert.That(bytes[13]).IsEqualTo((byte)63);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))).IsEqualTo((uint)(size - 12));
        await Assert.That(Convert.ToHexString(bytes.AsSpan(26, 20))).IsEqualTo(hash.ToUpperInvariant());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task CommonVersionUsesTwentySixPayloadBytes(int flags)
    {
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = ReplayMetadataChunk.Encode(bytes, new Version(0, 1, 0, 0), (byte)flags);
        await Assert.That(size).IsEqualTo(38);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes)).IsEqualTo(0xBB111201u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))).IsEqualTo(0x534b4950u);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))).IsEqualTo(26u);
        await Assert.That(Convert.ToHexString(bytes.AsSpan(12, 5))).IsEqualTo($"00{flags:X2}000100");
        await Assert.That(Convert.ToHexString(bytes.AsSpan(17, 20))).IsEqualTo(new string('0', 40));
    }

    [Test]
    [Arguments(127, 128, 16384, 65535)]
    [Arguments(65535, 65535, 65535, 65535)]
    [Arguments(1, 2, 3, 0)]
    public async Task VersionComponentsRoundTrip(int major, int minor, int build, int revision)
    {
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = ReplayMetadataChunk.Encode(bytes, new Version(major, minor, build, revision), 0xff);
        using var reader = new BinaryReader(new MemoryStream(bytes, 12, size - 12));
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)(revision != 0 ? 7 : 3));
        await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(major);
        await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(minor);
        await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(build);
        if (revision != 0)
        {
            await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(revision);
        }
        await Assert.That(Convert.ToHexString(reader.ReadBytes(20))).IsEqualTo(new string('0', 40));
        await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(0L);
        await Assert.That(reader.BaseStream.Position).IsEqualTo(reader.BaseStream.Length);
    }

    [Test]
    [Arguments(false, false, 0L)]
    [Arguments(true, false, 0L)]
    [Arguments(false, true, 1794142800123L)]
    [Arguments(true, true, 1794142800123L)]
    [Arguments(true, true, long.MaxValue)]
    public async Task OptionalTimestampsRoundTrip(bool start, bool stop, long timestamp)
    {
        var bytes = new byte[ReplayMetadataChunk.MaxSize];
        var size = ReplayMetadataChunk.Encode(bytes, new Version(0, 1, 0, 0), 3,
            start ? timestamp : null, stop ? timestamp : null);
        using var reader = new BinaryReader(new MemoryStream(bytes, 12, size - 12));
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)0);
        await Assert.That(reader.ReadByte()).IsEqualTo((byte)(3 | (start ? 16 : 0) | (stop ? 32 : 0)));
        await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(0);
        await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(1);
        await Assert.That(reader.Read7BitEncodedInt()).IsEqualTo(0);
        await Assert.That(Convert.ToHexString(reader.ReadBytes(20))).IsEqualTo(new string('0', 40));
        await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(0L);
        if (start)
        {
            await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(timestamp);
        }
        if (stop)
        {
            await Assert.That(reader.Read7BitEncodedInt64()).IsEqualTo(timestamp);
        }
        await Assert.That(reader.BaseStream.Position).IsEqualTo(reader.BaseStream.Length);
        if (timestamp == 1794142800123L && start && stop)
        {
            await Assert.That(size).IsEqualTo(50);
        }
    }
}
