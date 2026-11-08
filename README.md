# Always Official

Always Official is a TrackMania United Forever mod that keeps restarting official mode automatically after you jump into it once.

- By default, race is still in offline mode
- *Restart in Official Mode* starts official mode as usual
- While in official mode: retire (full respawn) automatically starts official mode again
- Input presses are carried over, so you dont have to re-press them again

It is the first C# mod integrated with TMF ModLoader (using NativeAOT).

## Building

Needs the .NET 10 SDK and Visual Studio (or its Build Tools) with "Desktop development with C++".

```sh
dotnet publish AlwaysOfficial -c Release [-o <dir>]
```

- `AlwaysOfficial\bin\Release\net10.0\win-x86\publish\` (or `<dir>`): `AlwaysOfficial.dll` and `d3d9.dll`, with PDBs.
- `dist\`: the same DLLs and the two packages:
  - `AlwaysOfficial-modloader.zip`: `AlwaysOfficial.dll` for the TrackMania ModLoader.
  - `AlwaysOfficial-standalone.zip`: `d3d9.dll` (loader) and `AlwaysOfficial.dll`, to place next to `TmForever.exe`.

## Debugging in VS Code

Needs the C/C++ extension (`ms-vscode.cpptools`, recommended by the workspace). The game folder differs per
machine, so set it once in your **User** settings (`Preferences: Open User Settings (JSON)`), not in the
repository's `.vscode/settings.json`:

```json
"alwaysOfficial.gameDirectory": "C:\\Program Files (x86)\\TmUnitedForever"
```

The publish refuses to run while the game is
open or when the folder holds another mod's `d3d9.dll` or a foreign `AlwaysOfficial.dll`. A game installed under
`Program Files` needs VS Code to run as administrator to write there.

## Testing

```sh
dotnet test --project AlwaysOfficial.Tests/AlwaysOfficial.Tests.csproj -c Release
```

Building the tests first publishes the mod twice in the same configuration: the normal build (into `dist\`) and a
native test build (`-p:NativeTestBuild=true`, into `bin\<Configuration>-NativeTests\`) that adds a test-only export.
Pass `-p:PublishModForTests=false` to skip this when both are already published.

## License

Licensed under the [MIT License](LICENSE).
