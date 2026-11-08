# Builds the mod: one native 32-bit DLL in dist\.
#
#   powershell -ExecutionPolicy Bypass -File build.ps1 [-Configuration Release|Debug] [-Tests] [-Version 0.1.0]
#
# Needs the .NET 10 SDK and Visual Studio (or its Build Tools) with "Desktop development with C++"
# (the MSVC x86/x64 tools): Native AOT links with link.exe, and native\shim.c is compiled by cl.exe.
# Behavioral checks: dotnet test --project tests/AlwaysOfficial.Tests/AlwaysOfficial.Tests.csproj

param(
  [ValidateSet("Release", "Debug")] [string]$Configuration = "Release",
  [Alias("NativeTests")] [switch]$Tests,
  [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$name = "AlwaysOfficial"

# Visual Studio 2026's vcvarsall.bat calls vswhere.exe by name; the AOT targets run it through
# findvcvarsall.bat and fail to find link.exe unless vswhere's folder is on PATH.
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path "$installer\vswhere.exe") { $env:PATH = "$installer;$env:PATH" }
else { throw "vswhere.exe not found in $installer. Install Visual Studio (or the Build Tools) with C++." }

$versionArguments = @()
if ($Version) { $versionArguments = @("-p:Version=$Version") }
dotnet publish "$root\src\$name\$name.csproj" -c $Configuration -r win-x86 -o "$root\build\$Configuration" --nologo --disable-build-servers -p:MSBuildEnableWorkloadResolver=false @versionArguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
Copy-Item "$root\build\$Configuration\$name.dll" "$dist\$name.dll" -Force
if (Test-Path "$root\build\$Configuration\$name.pdb") { Copy-Item "$root\build\$Configuration\$name.pdb" "$dist\$name.pdb" -Force }
Write-Host "Built $dist\$name.dll" -ForegroundColor Green

if ($Tests) {
  # Build the same hook code with one test-only image-base export, outside dist/.
  dotnet publish "$root\src\$name\$name.csproj" -c $Configuration -r win-x86 -o "$root\build\native-tests" --nologo --disable-build-servers -p:MSBuildEnableWorkloadResolver=false -p:NativeTestBuild=true
  if ($LASTEXITCODE -ne 0) { throw "native test DLL build failed ($LASTEXITCODE)" }
  dotnet test --project "$root\tests\AlwaysOfficial.Tests\AlwaysOfficial.Tests.csproj" --configuration $Configuration -p:MSBuildEnableWorkloadResolver=false
  if ($LASTEXITCODE -ne 0) { throw "TUnit checks failed ($LASTEXITCODE)" }
}
