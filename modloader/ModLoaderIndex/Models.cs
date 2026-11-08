using System.Text.Json.Serialization;

namespace AlwaysOfficial.ModLoaderIndex;

/// <summary>An entry of the GitHub REST "list releases" response (only the fields the feed needs).</summary>
public sealed record Release(string TagName, bool Draft, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets);

public sealed record ReleaseAsset(string Name, string Url, string BrowserDownloadUrl, string? Digest = null);

/// <summary>A TMLoader feed. modloader/index.json is the template: no versions, and a version_template.</summary>
public sealed record Feed(int Api, string Game, IReadOnlyDictionary<string, Product> Products);

/// <param name="VersionTemplate">Template only: what every version shares (executable, dependencies).</param>
public sealed record Product(string Name, string Author, string Type, string Description, string Homepage,
    IReadOnlyDictionary<string, ProductVersion> Versions, ProductVersion? VersionTemplate = null);

/// <summary>A published version; the template's version has no download link or hash.</summary>
public sealed record ProductVersion(string Executable, IReadOnlyList<Dependency> Dependencies, string? Changelog = null,
    string? DownloadLink = null, string? HashSha256 = null);

public sealed record Dependency(string Id, string Version);

/// <summary>GitHub and TMLoader both use snake_case; required and non-null members are enforced on read.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(Release[]))]
[JsonSerializable(typeof(Feed))]
internal sealed partial class IndexJsonContext : JsonSerializerContext;
