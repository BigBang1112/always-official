namespace AlwaysOfficial;

public static class ModInfo
{
    public const string Name = "AlwaysOfficial";
    public const string DisplayName = "Always Official";

    public static string Version => typeof(ModInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
