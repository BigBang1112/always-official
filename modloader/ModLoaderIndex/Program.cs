// Generates the TMLoader JSON feeds from this repository's published GitHub releases.
//
//   dotnet run --project modloader/ModLoaderIndex -- [--out site] [--repository owner/repository]
//
// GITHUB_REPOSITORY is the default repository; GH_TOKEN, when set, authenticates the API calls.
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AlwaysOfficial.ModLoaderIndex;

var outDir = "site";
var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outDir = args[++i];
            break;
        case "--repository" when i + 1 < args.Length:
            repository = args[++i];
            break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}. Usage: [--out <dir>] [--repository owner/repository]");
            return 2;
    }
}

if (!Regex.IsMatch(repository, @"^[\w.-]+/[\w.-]+$"))
{
    Console.Error.WriteLine("Repository must be owner/repository (--repository or GITHUB_REPOSITORY)");
    return 2;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AlwaysOfficial", null));
var token = Environment.GetEnvironmentVariable("GH_TOKEN");
if (!string.IsNullOrEmpty(token))
{
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
}

var releases = new List<Release>();
for (var page = 1; ; page++)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases?per_page=100&page={page}");
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    using var response = await http.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Listing releases of {repository} failed: {(int)response.StatusCode} {response.ReasonPhrase}");
        return 1;
    }

    var batch = await response.Content.ReadFromJsonAsync(IndexJsonContext.Default.ReleaseArray) ?? [];
    releases.AddRange(batch);
    if (batch.Length < 100)
    {
        break;
    }
}

await IndexBuilder.BuildAsync(releases, async asset =>
{
    using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
    using var response = await http.SendAsync(request);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadAsByteArrayAsync();
}, Path.Combine(AppContext.BaseDirectory, "index.json"), outDir);
Console.WriteLine($"Wrote ModLoader feeds for {releases.Count} releases to {Path.GetFullPath(outDir)}");
return 0;
