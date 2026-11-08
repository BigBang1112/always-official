namespace AlwaysOfficial.Core;

internal static unsafe class UserData
{
    public static bool SupportedLayout()
    {
        var code = (byte*)Memory.Rebase(0x0041bec0);
        ReadOnlySpan<byte> expected = [0x8b, 0x41, 0x28, 0x85, 0xc0, 0x75, 0x03, 0x8b, 0x41, 0x50, 0xc3];
        return sizeof(nint) == 4 && Memory.Readable(code, expected.Length) &&
            new ReadOnlySpan<byte>(code, expected.Length).SequenceEqual(expected);
    }

    public static string? GetDirectory()
    {
        return TryRead((nint)Memory.Rebase(0x00d73300), out nint mainEngine)
            ? GetDirectory(mainEngine) : null;
    }

    internal static string? GetDirectory(nint mainEngine)
    {
        if (mainEngine == 0 || !TryRead(mainEngine + 0x20, out uint count) || count <= 0x0b ||
            !TryRead(mainEngine + 0x24, out nint engines) || engines == 0 ||
            !TryRead(engines + 0x0b * sizeof(nint), out nint system) || system == 0 ||
            !TryRead(system + 0x28, out nint user))
        {
            return null;
        }

        if (user == 0 && !TryRead(system + 0x50, out user))
        {
            return null;
        }

        if (user == 0 || !TryRead(user + 0x40, out int length) || length <= 0 || length > 32767 ||
            !TryRead(user + 0x44, out nint chars) || chars == 0 || !Memory.Readable((void*)chars, length * 2))
        {
            return null;
        }

        var path = new string((char*)chars, 0, length);
        return path.IndexOf('\0') < 0 && Path.IsPathFullyQualified(path) ? path : null;
    }

    private static bool TryRead<T>(nint address, out T value) where T : unmanaged
    {
        return Memory.TryRead((void*)address, out value);
    }
}
