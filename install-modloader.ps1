# Installs the built mod into the TrackMania ModLoader (TMLoader).
# The loader stores each mod version under database/TmForever/products/<mod>/<version>/.
param(
  [string]$Dll = "",
  [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$name = "AlwaysOfficial"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $Dll) { $Dll = Join-Path $root "dist\$name.dll" }
if (-not (Test-Path $Dll)) { throw "$Dll not found. Build first: .\build.ps1" }
if (Get-Process -Name TmForever -ErrorAction SilentlyContinue) { throw "TrackMania is running. Close the game first." }
if (-not $Version) { $Version = (Get-Item $Dll).VersionInfo.ProductVersion -replace '\+.*$', '' }

$loader = "$env:LOCALAPPDATA\TMLoader"
if (-not (Test-Path $loader)) {
  throw "The TrackMania ModLoader is not installed ($loader is missing). Get it from https://tomashu.dev/software/tmloader/"
}
$product = "$loader\database\TmForever\products\$name"
$target = "$product\$Version"
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item "$root\modloader\description.yaml" "$product\description.yaml"
Copy-Item "$root\modloader\version.yaml" "$target\description.yaml"
Copy-Item $Dll "$target\$name.dll"
Write-Host "Installed Always Official $Version to $target" -ForegroundColor Green
Write-Host "Open the ModLoader, tick 'Always Official' and start the game. Log: $name.log in the game's active user-data directory."
