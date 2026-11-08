using System.Buffers.Binary;

namespace AlwaysOfficial.Core;

internal static class CallPatch
{
    public static bool Matches(ReadOnlySpan<byte> bytes, uint site, uint target)
    {
        return bytes.Length >= 5 && bytes[0] == 0xe8 &&
            unchecked(site + 5 + BinaryPrimitives.ReadUInt32LittleEndian(bytes[1..])) == target;
    }

    public static byte[] Encode(uint site, uint target)
    {
        byte[] bytes = [0xe8, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), unchecked(target - site - 5));
        return bytes;
    }
}
