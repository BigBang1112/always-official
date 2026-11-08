# Standalone mode: installs d3d9.dll (native loader) and AlwaysOfficial.dll (the mod).
#
#   powershell -ExecutionPolicy Bypass -File install-standalone.ps1 -GameDir "C:\Program Files (x86)\TmNationsForever"
#   powershell -ExecutionPolicy Bypass -File install-standalone.ps1 -GameDir ... -Uninstall

param(
  [Parameter(Mandatory = $true)] [string]$GameDir,
  [string]$Dll = "",
  [string]$LoaderDll = "",
  [switch]$Uninstall
)

$ErrorActionPreference = "Stop"
$name = "AlwaysOfficial"
if (-not $Dll) { $Dll = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "dist\$name.dll" }
if (-not $LoaderDll) { $LoaderDll = Join-Path (Split-Path -Parent $Dll) 'd3d9.dll' }

if (-not (Test-Path "$GameDir\TmForever.exe")) { throw "TmForever.exe not found in $GameDir" }
if (Get-Process -Name TmForever -ErrorAction SilentlyContinue) { throw "TrackMania is running. Close the game first." }

$target = "$GameDir\d3d9.dll"
$modTarget = Join-Path $GameDir "$name.dll"
function Is-OurProxy($path) {
  return (Test-Path -LiteralPath $path) -and (
    (Get-Item -LiteralPath $path).VersionInfo.ProductName -eq $name -or
    [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($path)).Contains('AlwaysOfficialStandaloneProxy'))
}
$ours = Is-OurProxy $target
if ((Test-Path -LiteralPath $modTarget) -and (Get-Item -LiteralPath $modTarget).VersionInfo.ProductName -ne $name) {
  throw "$modTarget is not ${name}: left alone"
}

if ($Uninstall) {
  if ((Test-Path $target) -and -not $ours) { throw "$target is not $name (another mod?): left alone" }
  if ($ours) { Remove-Item -LiteralPath $target }
  if (Test-Path -LiteralPath $modTarget) { Remove-Item -LiteralPath $modTarget }
  Write-Host "Removed the Always Official standalone loader and mod." -ForegroundColor Green
  return
}

if (-not (Test-Path $Dll)) { throw "$Dll not found. Build first: .\build.ps1" }
if (-not (Is-OurProxy $LoaderDll)) { throw "$LoaderDll is not the Always Official standalone loader. Build first: .\build.ps1" }
if ((Test-Path $target) -and -not $ours) {
  throw "$target exists and is not $name (another mod's standalone proxy). Use the ModLoader instead, or remove it first."
}
Copy-Item -LiteralPath $Dll -Destination $modTarget -Force
Copy-Item -LiteralPath $LoaderDll -Destination $target -Force
Write-Host "Installed $target and $modTarget. Start the game; log: $name.log in the game's active user-data directory." -ForegroundColor Green
