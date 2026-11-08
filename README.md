# Always Official

## Building

Needs the .NET 10 SDK and Visual Studio (or its Build Tools) with "Desktop development with C++".

```sh
dotnet publish src/AlwaysOfficial -c Release [-o <dir>]
```

- `src\AlwaysOfficial\bin\Release\net10.0\win-x86\publish\` (or `<dir>`): `AlwaysOfficial.dll` and `d3d9.dll`, with PDBs.
- `dist\`: the same DLLs, `version.yaml`, and the two packages:
  - `AlwaysOfficial-modloader.zip`: `AlwaysOfficial.dll` for the TrackMania ModLoader.
  - `AlwaysOfficial-standalone.zip`: `d3d9.dll` (loader) and `AlwaysOfficial.dll`, to place next to `TmForever.exe`.

## Testing

```sh
dotnet test --project tests/AlwaysOfficial.Tests/AlwaysOfficial.Tests.csproj -c Release
```

Building the tests first publishes the mod twice in the same configuration: the normal build (into `dist\`) and a
native test build (`-p:NativeTestBuild=true`, into `bin\<Configuration>-NativeTests\`) that adds a test-only export.
Pass `-p:PublishModForTests=false` to skip this when both are already published.
