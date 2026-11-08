using System.Buffers.Binary;

namespace AlwaysOfficial;

internal static class ReplayMetadataChunk
{
    public const uint Id = 0xBB111201;
    public const int MaxSize = 64;

    public static int Encode(Span<byte> destination, Version version, byte carriedInputs,
        ReadOnlySpan<byte> gitCommit = default, long startedAt = 0, long stoppedAt = 0,
        bool official = false)
    {
        if (!gitCommit.IsEmpty && gitCommit.Length != 20)
        {
            throw new ArgumentException("A Git SHA-1 commit must contain 20 bytes.", nameof(gitCommit));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, Id);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], 0x534b4950);

        var revision = Math.Max(version.Revision, 0);
        var offset = 12;

        destination[offset++] = 0;
        destination[offset++] = (byte)((carriedInputs & 3) | (revision != 0 ? 4 : 0) |
            (stoppedAt != 0 ? 8 : 0) | (official ? 16 : 0));

        WriteVersion(destination, ref offset, version.Major);
        WriteVersion(destination, ref offset, version.Minor);
        WriteVersion(destination, ref offset, Math.Max(version.Build, 0));

        if (revision != 0)
        {
            WriteVersion(destination, ref offset, revision);
        }

        destination.Slice(offset, 20).Clear();
        gitCommit.CopyTo(destination[offset..]);

        offset += 20;

        WriteTimestamp(destination, ref offset, startedAt);

        if (stoppedAt != 0)
        {
            WriteTimestamp(destination, ref offset, stoppedAt);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], (uint)(offset - 12));

        return offset;
    }

    private static void WriteVersion(Span<byte> destination, ref int offset, int value)
    {
        if ((uint)value > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        WriteUnsigned(destination, ref offset, (ulong)value);
    }

    private static void WriteTimestamp(Span<byte> destination, ref int offset, long value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        WriteUnsigned(destination, ref offset, (ulong)value);
    }

    private static void WriteUnsigned(Span<byte> destination, ref int offset, ulong value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            destination[offset++] = (byte)(next | (value != 0 ? 0x80 : 0));
        } while (value != 0);
    }
}
