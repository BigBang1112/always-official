$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$work = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $releases = @()
    foreach ($version in @('0.1.0', '0.2.0-beta.1', '0.3.0')) {
        $tag = "v$version"
        New-Item -ItemType Directory -Path "$work/$tag" | Out-Null
        [IO.File]::WriteAllText("$work/$tag/AlwaysOfficial.dll", "fixture DLL $version")
        Copy-Item "$PSScriptRoot/version.yaml" "$work/$tag/version.yaml"
        Compress-Archive -LiteralPath "$work/$tag/AlwaysOfficial.dll" -DestinationPath "$work/$tag/AlwaysOfficial-modloader.zip"
        $releases += @{
            tag_name = $tag
            draft = $version -eq '0.3.0'
            prerelease = $version.Contains('-')
            assets = @(@{
                name = 'AlwaysOfficial-modloader.zip'
                browser_download_url = "https://github.com/example/mod/releases/download/$tag/AlwaysOfficial-modloader.zip"
            }, @{ name = 'version.yaml' })
        }
    }
    $releases += @{ tag_name = 'v0.0.1'; assets = @() }
    $releases | ConvertTo-Json -Depth 10 | Set-Content "$work/releases.json"
    & "$PSScriptRoot/build-index.ps1" -OutDir "$work/site" -ReleasesFile "$work/releases.json" -AssetsDirectory $work
    $stable = Get-Content -Raw "$work/site/modloader/index.json" | ConvertFrom-Json
    $beta = Get-Content -Raw "$work/site/modloader/beta/index.json" | ConvertFrom-Json
    $versions = $stable.products.AlwaysOfficial.versions
    if ($stable.api -ne 1 -or $stable.game -ne 'TmForever' -or $stable.products.AlwaysOfficial.name -ne 'Always Official') { throw 'Wrong server schema' }
    if (@($versions.PSObject.Properties).Count -ne 1 -or -not $versions.'0.1.0') { throw 'Wrong stable releases' }
    if (@($beta.products.AlwaysOfficial.versions.PSObject.Properties).Count -ne 2 -or -not $beta.products.AlwaysOfficial.versions.'0.2.0-beta.1') { throw 'Wrong beta releases' }
    $entry = $versions.'0.1.0'
    $archive = "$work/v0.1.0/AlwaysOfficial-modloader.zip"
    if ($entry.hash_sha256 -cne (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Wrong ZIP digest' }
    if ($entry.download_link -ne 'https://github.com/example/mod/releases/download/v0.1.0/AlwaysOfficial-modloader.zip') { throw 'Wrong download URL' }
    if ($entry.dependencies.Count -ne 1 -or $entry.dependencies[0].id -ne 'CoreMod' -or $entry.dependencies[0].version -ne '^1.0.1') { throw 'Dependencies lost' }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        if ($zip.Entries.Count -ne 1 -or $zip.Entries[0].FullName -ne 'AlwaysOfficial.dll') { throw 'ZIP must contain only the DLL at its root' }
        $reader = New-Object IO.StreamReader($zip.Entries[0].Open())
        try {
            if ($reader.ReadToEnd() -ne 'fixture DLL 0.1.0') { throw 'Wrong DLL bytes' }
        } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    $publishedFiles = @(Get-ChildItem "$work/site" -Recurse -File)
    if ($publishedFiles.Count -ne 2 -or @($publishedFiles | Where-Object Extension -ne '.json').Count) { throw 'Pages must contain only the two JSON feeds' }
    Write-Host 'JSON ModLoader index checks passed.'
} finally {
    # Only the new GUID-named fixture directory under the system temp directory.
    $resolved = [IO.Path]::GetFullPath($work)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (-not $resolved.StartsWith($tempRoot) -or (Split-Path -Leaf $resolved) -notmatch '^[0-9a-f-]{36}$') { throw 'Unexpected fixture path' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
