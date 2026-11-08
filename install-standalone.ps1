# Standalone mode, no ModLoader: copies the mod next to TmForever.exe as d3d9.dll. Windows then
# loads it as the game's Direct3D 9; the mod forwards every d3d9 call to the system d3d9.dll.
#
#   powershell -ExecutionPolicy Bypass -File install-standalone.ps1 -GameDir "C:\Program Files (x86)\TmNationsForever"
#   powershell -ExecutionPolicy Bypass -File install-standalone.ps1 -GameDir ... -Uninstall

param(
  [Parameter(Mandatory = $true)] [string]$GameDir,
  [string]$Dll = "",
  [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$name = "AlwaysOfficial"
if (-not $Dll) { $Dll = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "dist\$name.dll" }

if (-not (Test-Path "$GameDir\TmForever.exe")) { throw "TmForever.exe not found in $GameDir" }
if (Get-Process -Name TmForever -ErrorAction SilentlyContinue) { throw "TrackMania is running. Close the game first." }

$target = "$GameDir\d3d9.dll"
$ours = (Test-Path $target) -and ((Get-Item $target).VersionInfo.ProductName -eq $name)

if ($Uninstall) {
  if (-not (Test-Path $target)) { Write-Host "Nothing to remove: no d3d9.dll in $GameDir"; return }
  if (-not $ours) { throw "$target is not $name (another mod?): left alone" }
  Remove-Item $target
  Write-Host "Removed $target" -ForegroundColor Green
  return
}

if (-not (Test-Path $Dll)) { throw "$Dll not found. Build first: .\build.ps1" }
if ((Test-Path $target) -and -not $ours) {
  throw "$target exists and is not $name (another mod's standalone proxy). Use the ModLoader instead, or remove it first."
}
Copy-Item $Dll $target -Force
Write-Host "Installed $target. Start the game; log: $name.log in the game's active user-data directory." -ForegroundColor Green
