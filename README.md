# Always Official

Always Official is a TrackMania United Forever mod that keeps restarting official mode automatically after you jump into it once.

The **Restart in Official Mode** feature came out a long time ago as Nadeo's first record leaderboard system in TrackMania. It was made opt-in most likely due to uncertainty, but the times have changed since and this feature made TMUF hunting experience just worse. So this mod reduces a few of the annoyances.

- By default, race is still in offline mode
- **Restart in Official Mode** starts official mode as usual
- While in official mode: retire (full respawn) or **Improve** button automatically starts official mode again
- Input presses are carried over, so you dont have to re-press them again
- Coppers are consumed only at the end of the countdown (`StartOfficialRecord`)
  - ...so this is not a free Official Mode mod...

It is the first C# mod integrated with TMF ModLoader (using NativeAOT, and only a little over 800kB).

## Installation

ModLoader is the recommended approach, as standalone can potentially collide with other mods.

### ModLoader

Close the ModLoader, open `%LOCALAPPDATA%\TMLoader\config.yaml` and add one of these URLs under `servers:`. Choose one of these:

- `https://bigbang1112.cz/trackmania/tmf/modloader/`
- `https://bigbang1112.github.io/always-official/modloader/`

Second URL may be more up to date when new updates come, but won't include my other mods in the future.

The mod should now appear in ModLoader where you can tick it. It should be compatible with all known mods.

### Standalone

If you don't like ModLoader (Competition Patch 2 is soon enforced and only on ModLoader so you will have to like it), BUT if you still want to install this mod standalone, you can.

Download the latest `AlwaysOfficial-standalone.v*.zip` from [Releases](https://github.com/BigBang1112/always-official/releases) and extract it into your TMUF installation folder. That should be it.

## Data

This mod stores additional data in the ghosts:

- Version of the mod
- Carried-over accel/brake input status
- Timestamps of the race start and finish in milliseconds
- Indication whether the run entered official mode

## Build

Needs the .NET 10 SDK and Visual Studio (or its Build Tools) with "Desktop development with C++".

```sh
dotnet publish AlwaysOfficial -c Release [-o <dir>]
```

- `AlwaysOfficial\bin\Release\net10.0\win-x86\publish\` (or `<dir>`): `AlwaysOfficial.dll` and `d3d9.dll`, with PDBs.
- `dist\`: the same DLLs, `AlwaysOfficial-modloader.v<version>.zip` (mod DLL only), and `AlwaysOfficial-standalone.v<version>.zip` (both DLLs, to place next to `TmForever.exe`).

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
