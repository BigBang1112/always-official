using System.Text.Json.Serialization;

namespace AlwaysOfficial.ModLoaderIndex;

/// <summary>The GitHub REST "latest release" response (only the fields the feed needs).</summary>
public sealed record Release(string TagName, string? Body, IReadOnlyList<ReleaseAsset> Assets);

/// <param name="Digest">"sha256:&lt;hex&gt;", computed by GitHub on upload.</param>
public sealed record ReleaseAsset(string Name, string BrowserDownloadUrl, string? Digest = null);

/// <summary>A TMLoader feed.</summary>
public sealed record Feed(int Api, string Game, IReadOnlyDictionary<string, Product> Products);

public sealed record Product(string Name, string Author, string Type, string Description, string Homepage,
    IReadOnlyDictionary<string, ProductVersion> Versions);

public sealed record ProductVersion(string Executable, IReadOnlyList<Dependency> Dependencies, string? Changelog,
    string DownloadLink, string HashSha256);

public sealed record Dependency(string Id, string Version);

/// <summary>GitHub and TMLoader both use snake_case; required and non-null members are enforced on read.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Release))]
[JsonSerializable(typeof(Feed))]
internal sealed partial class IndexJsonContext : JsonSerializerContext;
