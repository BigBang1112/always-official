// Writes the TMLoader feed (<out>/modloader/index.json) with the latest GitHub release as its only version.
//
//   dotnet run --project modloader/ModLoaderIndex -- [out, default: site]
//
// GITHUB_REPOSITORY (owner/repository) names the repository; GH_TOKEN, when set, authenticates the API call.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlwaysOfficial.ModLoaderIndex;

const string archiveName = "AlwaysOfficial-modloader.zip";

var outDir = args.Length > 0 ? args[0] : "site";
var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
if (string.IsNullOrEmpty(repository))
{
    Console.Error.WriteLine("Set GITHUB_REPOSITORY to owner/repository");
    return 2;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlwaysOfficial", null));
http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
var token = Environment.GetEnvironmentVariable("GH_TOKEN");
if (!string.IsNullOrEmpty(token))
{
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}

var release = await http.GetFromJsonAsync($"https://api.github.com/repos/{repository}/releases/latest", IndexJsonContext.Default.Release)
    ?? throw new InvalidDataException("Empty latest release response");
var archive = release.Assets.FirstOrDefault(a => a.Name == archiveName)
    ?? throw new InvalidDataException($"Release {release.TagName} has no {archiveName}");
if (archive.Digest is not { } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal))
{
    throw new InvalidDataException($"{archiveName} of {release.TagName} has no SHA-256 digest");
}

var feed = new Feed(
    Api: 1,
    Game: "TmForever",
    Products: new Dictionary<string, Product>
    {
        ["AlwaysOfficial"] = new(
            Name: "Always Official",
            Author: "BigBang1112",
            Type: "modification",
            Description: "After entering official mode manually, restart subsequent solo runs in official mode without repeating the confirmation.",
            Homepage: "https://github.com/BigBang1112/always-official",
            Versions: new Dictionary<string, ProductVersion>
            {
                [release.TagName.TrimStart('v')] = new(
                    Executable: "AlwaysOfficial.dll",
                    Dependencies: [new Dependency("CoreMod", "^1.0.1")],
                    Changelog: string.IsNullOrWhiteSpace(release.Body) ? null : release.Body,
                    DownloadLink: archive.BrowserDownloadUrl,
                    HashSha256: digest["sha256:".Length..]),
            }),
    });

var path = Path.Combine(outDir, "modloader", "index.json");
Directory.CreateDirectory(Path.GetDirectoryName(path)!);
File.WriteAllText(path, JsonSerializer.Serialize(feed, IndexJsonContext.Default.Feed) + "\n");
Console.WriteLine($"Wrote {Path.GetFullPath(path)} for {release.TagName}");
return 0;
