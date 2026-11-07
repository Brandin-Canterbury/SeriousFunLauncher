# SeriousFun Launcher

One-click setup for the SeriousFun Ultima Online shard (`seriousfun.tplinkdns.com:2593`).

**Download:** [SeriousFunLauncher.exe](https://github.com/Brandin-Canterbury/SeriousFunLauncher/releases/latest/download/SeriousFunLauncher.exe). It runs on Windows 10/11 with nothing else to install.

Pick a folder for the UO Classic client and one for TazUO, then press **Play**. The launcher:

1. Installs the official UO Classic client from EA and patches it to the latest version.
2. Installs or updates [TazUO](https://github.com/Brandin-Canterbury/TazUO) from its latest release.
3. Downloads the shard's edited map files, if there are any (see below).
4. Writes TazUO's `settings.json` for the shard (server, UO folder, client version, encryption off).
5. Starts the game.

Settings are saved in `%AppData%\SeriousFunLauncher\launcher.json`.

## Shard map edits (adding or removing trees)

Trees that are part of the map live in the map files (`staticsN.mul` / `staidxN.mul`) on every player's PC. The server can't hide them, so after the map is edited every client needs the new files.

1. In-game, as an Administrator: `[Unfreeze` the area. That takes the trees out of the server's map files and turns them into items. `[delete` the trees you don't want, `[add Static 0xCCD` (or another tree ID) for new ones, and `[Freeze` the area to bake them back in. Back up `ClientFiles` on the server first.
2. On the main PC: `.\Publish-ShardData.ps1 -Maps 0` (0 Felucca, 1 Trammel, 2 Ilshenar, 3 Malas, 4 Tokuno, 5 Ter Mur). This copies those files from `\Phoenix-Server\GameServers\ServUO\ClientFiles` and uploads them to the `shard-data` release.
3. Players get them the next time they press **Play**. The launcher keeps them in `TazUO\ShardData` (out of reach of EA's patcher) and starts TazUO with `-uofilesoverride`. Starting TazUO.exe directly, without the launcher, uses EA's stock maps.

`.\Publish-ShardData.ps1 -Maps 0 -Remove` puts players back on EA's files for that map. The `shard-data` release must never be marked latest, because the download link above points at the latest release.

Trees placed with `[add` and left unfrozen need none of this: they are items the server sends to every client.

## Building

```
dotnet build -c Release
```

Output: `bin\Release\SeriousFunLauncher.exe`. Every push to `main` builds a new release.
