using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AlwaysOfficial.ModLoaderIndex;

/// <summary>Builds JSON-only TMLoader feeds referencing immutable GitHub release assets.</summary>
public static partial class IndexBuilder
{
    public const string ProductId = "AlwaysOfficial";
    public const string ArchiveName = "AlwaysOfficial-modloader.zip";
    public const string DllName = "AlwaysOfficial.dll";

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)$")]
    private static partial Regex TagPattern();

    /// <summary>Writes &lt;outDir&gt;/modloader/index.json (stable) and modloader/beta/index.json.</summary>
    public static async Task BuildAsync(IEnumerable<Release> releases, Func<ReleaseAsset, Task<byte[]>> download,
        string templatePath, string outDir)
    {
        Feed template;
        await using (var stream = File.OpenRead(templatePath))
        {
            template = await JsonSerializer.DeserializeAsync(stream, IndexJsonContext.Default.Feed)
                ?? throw new InvalidDataException($"Empty feed template: {templatePath}");
        }

        var product = template.Products.GetValueOrDefault(ProductId)
            ?? throw new InvalidDataException($"The feed template has no {ProductId} product");
        if (product.VersionTemplate?.Executable != DllName)
        {
            throw new InvalidDataException($"The feed template's version_template must run {DllName}");
        }

        var stable = new Dictionary<string, ProductVersion>();
        var beta = new Dictionary<string, ProductVersion>();
        foreach (var release in releases)
        {
            var tag = TagPattern().Match(release.TagName);
            var archive = release.Assets.FirstOrDefault(a => a.Name == ArchiveName);
            if (release.Draft || !tag.Success || archive is null)
            {
                continue;
            }

            if (!archive.BrowserDownloadUrl.StartsWith("https://", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Expected an HTTPS release download URL");
            }

            var bytes = await download(archive);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (archive.Digest is not null && archive.Digest != "sha256:" + hash)
            {
                throw new InvalidDataException($"Digest mismatch: {archive.Name} of {release.TagName}");
            }

            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                if (zip.Entries.Count != 1 || zip.Entries[0].FullName != DllName)
                {
                    throw new InvalidDataException($"Release ZIP must contain only {DllName} at its root");
                }
            }

            var version = tag.Groups[1].Value;
            beta[version] = product.VersionTemplate with { DownloadLink = archive.BrowserDownloadUrl, HashSha256 = hash };
            if (!release.Prerelease && !version.Contains('-'))
            {
                stable[version] = beta[version];
            }
        }

        foreach (var (channel, versions) in new[] { ("", stable), ("beta", beta) })
        {
            var published = product with { Versions = versions, VersionTemplate = null };
            var feed = template with { Products = new Dictionary<string, Product> { [ProductId] = published } };
            var directory = Path.Combine(outDir, "modloader", channel);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "index.json"),
                JsonSerializer.Serialize(feed, IndexJsonContext.Default.Feed) + "\n");
        }
    }
}
