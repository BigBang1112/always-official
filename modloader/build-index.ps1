# Build JSON-only TMLoader feeds referencing immutable GitHub release assets.
param(
    [string]$OutDir = 'site',
    [string]$Repository = $env:GITHUB_REPOSITORY,
    # Offline validation: GitHub-shaped release JSON and files/<tag>/<asset>.
    [string]$ReleasesFile = '',
    [string]$AssetsDirectory = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'AlwaysOfficial' }
if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }

function Get-ReleaseAsset($release, $asset, $destination) {
    if ($AssetsDirectory) {
        Copy-Item -LiteralPath (Join-Path $AssetsDirectory "$($release.tag_name)/$($asset.name)") -Destination $destination
    } else {
        $assetHeaders = $headers.Clone()
        $assetHeaders.Accept = 'application/octet-stream'
        Invoke-WebRequest -Uri $asset.url -Headers $assetHeaders -OutFile $destination
    }
    $digest = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.digest -and $asset.digest -ne "sha256:$digest") { throw "Digest mismatch: $($asset.name)" }
}

function Read-VersionMetadata($path) {
    # The project's metadata format: plain/quoted scalars and an id/version dependency list.
    # Reject unsupported YAML rather than silently publishing incomplete dependencies.
    $metadata = @{ dependencies = @() }
    $dependency = $null
    foreach ($line in Get-Content -LiteralPath $path) {
        if ($line -match '^\s*(#.*)?$' -or $line -eq 'dependencies:') { continue }
        if ($line -notmatch '^(?<indent>\s*)(?<list>- )?(?<key>\w+):\s+(?<value>.+?)\s*$') {
            throw "Unsupported metadata line: $line"
        }
        $key = $Matches.key
        $value = $Matches.value
        if ($value.StartsWith("'") -and $value.EndsWith("'")) { $value = $value.Substring(1, $value.Length - 2).Replace("''", "'") }
        elseif ($value.StartsWith('"')) { $value = $value | ConvertFrom-Json }
        if ($Matches.list) {
            if ($key -ne 'id') { throw 'A dependency must start with id' }
            $dependency = @{ id = $value }
            $metadata.dependencies += $dependency
        } elseif ($Matches.indent) {
            if ($null -eq $dependency -or $key -ne 'version') { throw 'Expected a dependency version' }
            $dependency.version = $value
        } else {
            if ($key -notin @('executable', 'changelog')) { throw "Unknown version field: $key" }
            $metadata[$key] = $value
        }
    }
    if ($metadata.executable -ne 'AlwaysOfficial.dll' -or
        @($metadata.dependencies | Where-Object { -not $_.id -or -not $_.version }).Count) {
        throw 'Invalid ModLoader version metadata'
    }
    return $metadata
}

if ($ReleasesFile) {
    $releases = Get-Content -Raw -LiteralPath $ReleasesFile | ConvertFrom-Json
} else {
    if ($Repository -notmatch '^[\w.-]+/[\w.-]+$') { throw 'Repository must be owner/repository' }
    $releases = @()
    for ($page = 1; ; $page++) {
        $batch = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/releases?per_page=100&page=$page" -Headers $headers
        $releases += $batch
        if ($batch.Count -lt 100) { break }
    }
}

New-Item -ItemType Directory -Force -Path "$OutDir/modloader/beta" | Out-Null
$stable = @{}
$beta = @{}
$work = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    foreach ($release in $releases) {
        if ($release.draft -or $release.tag_name -notmatch '^v(\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?)$') { continue }
        $version = $Matches[1]
        $archive = $release.assets | Where-Object name -eq 'AlwaysOfficial-modloader.zip' | Select-Object -First 1
        $yaml = $release.assets | Where-Object name -eq 'version.yaml' | Select-Object -First 1
        if (-not $archive -or -not $yaml) { continue }
        if ($archive.browser_download_url -notmatch '^https://') { throw 'Expected an HTTPS release download URL' }
        $archivePath = "$work/AlwaysOfficial-modloader.zip"
        Get-ReleaseAsset $release $archive $archivePath
        Get-ReleaseAsset $release $yaml "$work/version.yaml"
        $entry = Read-VersionMetadata "$work/version.yaml"
        $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            if ($zip.Entries.Count -ne 1 -or $zip.Entries[0].FullName -ne 'AlwaysOfficial.dll') {
                throw 'Release ZIP must contain only AlwaysOfficial.dll at its root'
            }
        } finally { $zip.Dispose() }
        $entry.download_link = $archive.browser_download_url
        $entry.hash_sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        $beta[$version] = $entry
        if (-not $release.prerelease -and -not $version.Contains('-')) { $stable[$version] = $entry }
    }
    foreach ($channel in @('', 'beta')) {
        $index = Get-Content -Raw "$PSScriptRoot/index.json" | ConvertFrom-Json
        $index.products.AlwaysOfficial.versions = if ($channel) { $beta } else { $stable }
        $json = $index | ConvertTo-Json -Depth 20
        [IO.File]::WriteAllText([IO.Path]::GetFullPath("$OutDir/modloader/$channel/index.json"), $json + "`n")
    }
} finally {
    Get-ChildItem -LiteralPath $work -File | Remove-Item -Force
    Remove-Item -LiteralPath $work
}
