namespace AlwaysOfficial;

public static partial class ModInfo
{
    public const string Name = "AlwaysOfficial";
    public const string DisplayName = "Always Official";

    public static string Version => BuildVersion;

    internal static readonly Version LibraryVersion = new(BuildAssemblyVersion);
    internal static readonly byte[] GitCommit = BuildCommit.Length == 40
        ? Convert.FromHexString(BuildCommit)
        : new byte[20];
}
