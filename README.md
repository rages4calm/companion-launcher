# Companion Launcher

A shared launcher foundation for Carl's native game projects. The interface, settings
pages, profiles, launch handling and achievement browser live here once. Each game
supplies a small adapter for its engine and branding.

**Version 0.1.0** includes a portable .NET core, the familiar Windows Companion shell,
two local NuGet packages, a new-game generator and a runnable synthetic demo.
The source is MIT licensed. No ROM, game engine or game assets are included.

## Try it

The built demo is `samples/Companion.Demo/bin/Release/net8.0-windows/Companion.Demo.exe`.
It shows configurable pages, separate profiles and the sibling achievement toolkit's
earned/partial/locked example. Play runs a tiny process fixture and writes a receipt;
it does not run a game. Demo settings use their own `CompanionTemplateDemo` data root.

To rebuild on Windows with the .NET 8 SDK or newer and Python 3.10+:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

That command checks the core, generator and real Windows buttons, renders the demo,
builds two independent consumers and packs **Core** and **Windows** into `packages`.
It only starts synthetic process fixtures and writes isolated test data. Receipts and
versioned source/package ZIPs go in `dist`. For a separate visual smoke test:

```powershell
& .\samples\Companion.Demo\bin\Release\net8.0-windows\Companion.Demo.exe --smoke-test C:\Temp\CompanionDemo-fresh
```

The output directory must not exist. The receipt distinguishes rendered pages from
gameplay. Setup and quick-save resume are optional adapter capabilities; the ordinary
demo does not declare them. The separate Windows probe verifies both with synthetic
callbacks and an actual child process, including cancellation and refreshed settings.
They are not claims of a generic ROM converter or save format.

## Start the next game

```powershell
python .\scripts\new_game.py --id my-next-game --title "My Next Game" --output "..\My Next Game Companion"
dotnet build "..\My Next Game Companion\Game.Companion.csproj" -c Release
```

The generator creates a minimal `GameAdapter.cs`, app entry point, pinned package
references and a local feed config. It refuses to overwrite an existing folder.
No shared source is copied into the game. A shared fix becomes available to every
consumer by updating its tested package version.

The starter can launch an existing native executable with no arguments. Its adapter
must add that engine's setup, asset checks, native settings translation and session
arguments before use as a real game integration. Features appear only when declared.
See [ADAPTER-GUIDE.md](ADAPTER-GUIDE.md) for that boundary and concrete examples.

## Achievements

The sibling **CompanionAchievements** directory is the separate offline gameplay
toolkit. It evaluates each game's completed events and commits progress before a
notification. This launcher reads its schema-v1 catalog/progress JSON without awarding
or rewriting progress. Each game defines its own goals and popup/sound presentation.

No account, server or RetroAchievements dependency is required. The achievements
toolkit's C core remains 0.1.0; its authoring/distribution toolkit is 0.2.0.

## Existing games and platform scope

EarthBound, Zelda and Secret of the Stars have **not** been migrated or changed.
Their released save/settings paths and live installations stay as they are. A later
opt-in migration supplies an adapter for their exact native formats and verifies a
private install before replacing any launcher. Secret of the Stars remains paused.

The core targets portable .NET 8. This first GUI host uses Windows Forms; Android,
Linux and macOS GUI reuse needs a portable host and platform-specific launch/setup
adapters. Existing cross-platform game launchers are unaffected. The native C
achievement core is independent of the Windows GUI.

`dist` contains versioned source/package ZIPs and hash receipts after export.
The source ZIP excludes binaries, build/test data and all game content.
See [ARCHITECTURE.md](ARCHITECTURE.md) and [PROVENANCE.md](PROVENANCE.md).
