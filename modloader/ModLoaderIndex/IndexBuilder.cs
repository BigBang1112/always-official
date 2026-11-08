using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AlwaysOfficial.ModLoaderIndex;

public sealed record ReleaseAsset(string Name, string Url, string BrowserDownloadUrl, string? Digest);

public sealed record Release(string TagName, bool Draft, bool Prerelease, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Builds JSON-only TMLoader feeds referencing immutable GitHub release assets.</summary>
public static partial class IndexBuilder
{
    public const string ArchiveName = "AlwaysOfficial-modloader.zip";
    public const string MetadataName = "version.yaml";
    public const string DllName = "AlwaysOfficial.dll";

    [GeneratedRegex(@"^v(\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^\s*(#.*)?$")]
    private static partial Regex BlankLine();

    [GeneratedRegex(@"^(?<indent>\s*)(?<list>- )?(?<key>\w+):\s+(?<value>.+?)\s*$")]
    private static partial Regex MetadataLine();

    /// <summary>Parses the GitHub REST "list releases" response.</summary>
    public static IEnumerable<Release> ParseReleases(JsonElement releases)
    {
        foreach (var release in releases.EnumerateArray())
        {
            var assets = release.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString()!,
                asset.GetProperty("url").GetString()!,
                asset.GetProperty("browser_download_url").GetString()!,
                asset.TryGetProperty("digest", out var digest) ? digest.GetString() : null)).ToList();
            yield return new Release(release.GetProperty("tag_name").GetString()!,
                release.GetProperty("draft").GetBoolean(), release.GetProperty("prerelease").GetBoolean(), assets);
        }
    }

    /// <summary>Writes &lt;outDir&gt;/modloader/index.json (stable) and modloader/beta/index.json.</summary>
    public static async Task BuildAsync(IEnumerable<Release> releases, Func<ReleaseAsset, Task<byte[]>> download,
        string templatePath, string outDir)
    {
        var stable = new JsonObject();
        var beta = new JsonObject();
        foreach (var release in releases)
        {
            var tag = TagPattern().Match(release.TagName);
            if (release.Draft || !tag.Success)
            {
                continue;
            }

            var version = tag.Groups[1].Value;
            var archive = release.Assets.FirstOrDefault(a => a.Name == ArchiveName);
            var yaml = release.Assets.FirstOrDefault(a => a.Name == MetadataName);
            if (archive is null || yaml is null)
            {
                continue;
            }

            if (!archive.BrowserDownloadUrl.StartsWith("https://", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Expected an HTTPS release download URL");
            }

            var archiveBytes = await DownloadVerified(archive, download);
            var entry = ReadVersionMetadata(System.Text.Encoding.UTF8.GetString(await DownloadVerified(yaml, download)));
            using (var zip = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read))
            {
                if (zip.Entries.Count != 1 || zip.Entries[0].FullName != DllName)
                {
                    throw new InvalidDataException($"Release ZIP must contain only {DllName} at its root");
                }
            }

            entry["download_link"] = archive.BrowserDownloadUrl;
            entry["hash_sha256"] = Sha256(archiveBytes);
            beta[version] = entry.DeepClone();
            if (!release.Prerelease && !version.Contains('-'))
            {
                stable[version] = entry;
            }
        }

        foreach (var (channel, versions) in new[] { ("", stable), ("beta", beta) })
        {
            var index = JsonNode.Parse(await File.ReadAllTextAsync(templatePath))!;
            index["products"]!["AlwaysOfficial"]!["versions"] = versions;
            var directory = Path.Combine(outDir, "modloader", channel);
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "index.json"),
                index.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
    }

    /// <summary>
    /// The project's metadata format: plain/quoted scalars and an id/version dependency list.
    /// Rejects unsupported YAML rather than silently publishing incomplete dependencies.
    /// </summary>
    public static JsonObject ReadVersionMetadata(string text)
    {
        var metadata = new JsonObject();
        var dependencies = new JsonArray();
        JsonObject? dependency = null;
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (BlankLine().IsMatch(line) || line == "dependencies:")
            {
                continue;
            }

            var match = MetadataLine().Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException($"Unsupported metadata line: {line}");
            }

            var key = match.Groups["key"].Value;
            var value = Unquote(match.Groups["value"].Value);
            if (match.Groups["list"].Success)
            {
                if (key != "id")
                {
                    throw new InvalidDataException("A dependency must start with id");
                }

                dependency = new JsonObject { ["id"] = value };
                dependencies.Add(dependency);
            }
            else if (match.Groups["indent"].Length > 0)
            {
                if (dependency is null || key != "version")
                {
                    throw new InvalidDataException("Expected a dependency version");
                }

                dependency["version"] = value;
            }
            else
            {
                if (key is not ("executable" or "changelog"))
                {
                    throw new InvalidDataException($"Unknown version field: {key}");
                }

                metadata[key] = value;
            }
        }

        if ((string?)metadata["executable"] != DllName || dependencies.Any(d => d!["version"] is null))
        {
            throw new InvalidDataException("Invalid ModLoader version metadata");
        }

        metadata["dependencies"] = dependencies;
        return metadata;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('\'') && value.EndsWith('\''))
        {
            return value[1..^1].Replace("''", "'");
        }

        return value.StartsWith('"') ? JsonSerializer.Deserialize<string>(value)! : value;
    }

    private static async Task<byte[]> DownloadVerified(ReleaseAsset asset, Func<ReleaseAsset, Task<byte[]>> download)
    {
        var bytes = await download(asset);
        if (asset.Digest is not null && asset.Digest != "sha256:" + Sha256(bytes))
        {
            throw new InvalidDataException($"Digest mismatch: {asset.Name}");
        }

        return bytes;
    }

    private static string Sha256(byte[] bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}
