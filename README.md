# SeriousFun Launcher

One-click setup for the SeriousFun Ultima Online shard (`seriousfun.tplinkdns.com:2593`).

**Download:** [SeriousFunLauncher.exe](https://github.com/Brandin-Canterbury/SeriousFunLauncher/releases/latest/download/SeriousFunLauncher.exe). It runs on Windows 10/11 with nothing else to install.

Pick a folder for the UO Classic client and one for TazUO, then press **Play**. The launcher:

1. Installs the official UO Classic client from EA and patches it to the latest version.
2. Installs or updates [TazUO](https://github.com/Brandin-Canterbury/TazUO) from its latest release.
3. Writes TazUO's `settings.json` for the shard (server, UO folder, client version, encryption off).
4. Starts the game.

Settings are saved in `%AppData%\SeriousFunLauncher\launcher.json`.

## Building

```
dotnet build -c Release
```

Output: `bin\Release\SeriousFunLauncher.exe`. Every push to `main` builds a new release.
