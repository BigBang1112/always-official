using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlwaysOfficial.ModLoaderIndex;

namespace AlwaysOfficial.Tests;

public class ModLoaderIndexTests
{
    private static byte[] Archive(string version)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry(IndexBuilder.DllName).Open());
            writer.Write($"fixture DLL {version}");
        }

        return stream.ToArray();
    }

    [Test]
    [DisplayName("Feeds list stable and beta releases with verified archives and dependencies")]
    public async Task FeedsFromPublishedReleases()
    {
        var root = NativeTests.FindRoot();
        var yaml = await File.ReadAllBytesAsync(Path.Combine(root, "modloader", "version.yaml"));
        var files = new Dictionary<string, byte[]>();
        var releasesJson = new List<object>();
        foreach (var version in new[] { "0.1.0", "0.2.0-beta.1", "0.3.0" })
        {
            var tag = $"v{version}";
            files[$"{tag}/{IndexBuilder.ArchiveName}"] = Archive(version);
            files[$"{tag}/{IndexBuilder.MetadataName}"] = yaml;
            releasesJson.Add(new
            {
                tag_name = tag,
                draft = version == "0.3.0",
                prerelease = version.Contains('-'),
                assets = new[]
                {
                    new { name = IndexBuilder.ArchiveName, url = $"{tag}/{IndexBuilder.ArchiveName}", browser_download_url = $"https://github.com/example/mod/releases/download/{tag}/{IndexBuilder.ArchiveName}" },
                    new { name = IndexBuilder.MetadataName, url = $"{tag}/{IndexBuilder.MetadataName}", browser_download_url = $"https://github.com/example/mod/releases/download/{tag}/{IndexBuilder.MetadataName}" },
                },
            });
        }

        releasesJson.Add(new { tag_name = "v0.0.1", draft = false, prerelease = false, assets = Array.Empty<object>() });
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(releasesJson));
        var site = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            await IndexBuilder.BuildAsync(IndexBuilder.ParseReleases(json.RootElement),
                asset => Task.FromResult(files[asset.Url]), Path.Combine(root, "modloader", "index.json"), site);

            using var stable = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(site, "modloader", "index.json")));
            using var beta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(site, "modloader", "beta", "index.json")));
            var product = stable.RootElement.GetProperty("products").GetProperty("AlwaysOfficial");
            await Assert.That(stable.RootElement.GetProperty("api").GetInt32()).IsEqualTo(1);
            await Assert.That(stable.RootElement.GetProperty("game").GetString()).IsEqualTo("TmForever");
            await Assert.That(product.GetProperty("name").GetString()).IsEqualTo("Always Official");

            var stableVersions = product.GetProperty("versions").EnumerateObject().Select(p => p.Name).ToList();
            var betaVersions = beta.RootElement.GetProperty("products").GetProperty("AlwaysOfficial").GetProperty("versions")
                .EnumerateObject().Select(p => p.Name).ToList();
            await Assert.That(stableVersions).IsEquivalentTo(["0.1.0"]);
            await Assert.That(betaVersions).IsEquivalentTo(["0.1.0", "0.2.0-beta.1"]);

            var entry = product.GetProperty("versions").GetProperty("0.1.0");
            var digest = Convert.ToHexStringLower(SHA256.HashData(files[$"v0.1.0/{IndexBuilder.ArchiveName}"]));
            await Assert.That(entry.GetProperty("hash_sha256").GetString()).IsEqualTo(digest);
            await Assert.That(entry.GetProperty("download_link").GetString())
                .IsEqualTo($"https://github.com/example/mod/releases/download/v0.1.0/{IndexBuilder.ArchiveName}");
            await Assert.That(entry.GetProperty("executable").GetString()).IsEqualTo(IndexBuilder.DllName);
            var dependency = entry.GetProperty("dependencies").EnumerateArray().Single();
            await Assert.That(dependency.GetProperty("id").GetString()).IsEqualTo("CoreMod");
            await Assert.That(dependency.GetProperty("version").GetString()).IsEqualTo("^1.0.1");

            var published = Directory.GetFiles(site, "*", SearchOption.AllDirectories);
            await Assert.That(published.Length).IsEqualTo(2);
            await Assert.That(published.All(f => f.EndsWith(".json", StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            if (Directory.Exists(site))
            {
                Directory.Delete(site, recursive: true);
            }
        }
    }

    [Test]
    [DisplayName("Archives containing more than the DLL are rejected")]
    public async Task RejectsArchiveWithExtraFiles()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry(IndexBuilder.DllName);
            zip.CreateEntry("d3d9.dll");
        }

        var release = new Release("v1.0.0", false, false,
        [
            new ReleaseAsset(IndexBuilder.ArchiveName, "zip", "https://example.com/a.zip", null),
            new ReleaseAsset(IndexBuilder.MetadataName, "yaml", "https://example.com/version.yaml", null),
        ]);
        var yaml = Encoding.UTF8.GetBytes("executable: AlwaysOfficial.dll\n");
        await Assert.That(() => IndexBuilder.BuildAsync([release],
                asset => Task.FromResult(asset.Url == "zip" ? stream.ToArray() : yaml), "unused.json", "unused"))
            .Throws<InvalidDataException>();
    }

    [Test]
    [DisplayName("Unsupported version metadata is rejected")]
    [Arguments("executable: Other.dll\n")]
    [Arguments("executable: AlwaysOfficial.dll\nname: x\n")]
    [Arguments("executable: AlwaysOfficial.dll\ndependencies:\n  - id: CoreMod\n")]
    [Arguments("executable: AlwaysOfficial.dll\ndependencies:\n  - version: 1\n")]
    public async Task RejectsInvalidMetadata(string yaml)
    {
        await Assert.That(() => IndexBuilder.ReadVersionMetadata(yaml)).Throws<InvalidDataException>();
    }
}
