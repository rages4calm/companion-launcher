# Companion Launcher

[![Build and test](https://github.com/rages4calm/companion-launcher/actions/workflows/ci.yml/badge.svg)](https://github.com/rages4calm/companion-launcher/actions/workflows/ci.yml)

A reusable, offline launcher for native games. Share the settings UI, profiles,
setup workflow and achievement browser; keep each game's engine rules in a small
adapter. Built from the Companion launcher used by EarthBound and Zelda ports.

**MIT licensed. No ROMs, games, artwork, music or user saves are included.**

## Platforms

| Platform | Host | Requirements |
| --- | --- | --- |
| Windows | Existing Windows Forms shell | .NET 8; existing interface retained |
| Linux | Avalonia desktop shell | .NET 8; X11/XWayland and desktop graphics libraries |
| macOS | Avalonia desktop shell | .NET 8; Intel or Apple Silicon |
| Android | Avalonia single-view host + activity bridge | Android 8/API 26+; ARM64 or x64; .NET 10 Android workload to build |

Version **0.2.0** adds the portable host alongside Windows Forms. Windows users
keep the same launcher interface. Every host uses the same core settings and
achievement contracts. Existing game installations are not migrated by this SDK.
See [platform details and testing limits](PLATFORMS.md).

## Run an example

Desktop (Linux, macOS or Windows), with .NET 8 SDK or newer:

```sh
dotnet run --project samples/Companion.Desktop -c Release
```

Windows Forms example:

```powershell
dotnet run --project samples/Companion.Demo -c Release
```

The examples use synthetic settings and achievements. Run their setup, then Play
to exercise a tiny process/activity fixture. They are not game ports.

Android requires the .NET 10 Android workload, Android SDK and JDK 21:

```sh
dotnet workload install android
dotnet build samples/Companion.Android -c Release -p:RuntimeIdentifier=android-arm64
```

If SDK discovery needs help, pass `-p:AndroidSdkDirectory=...` and
`-p:JavaSdkDirectory=...`. The signed development APK is in that project's
`bin/Release/net10.0-android/android-arm64` directory. Release-store signing and
physical-device certification are the consuming project's responsibility.

## Use it in your game

The shared libraries are ordinary local NuGet packages. They are not on nuget.org
or GitHub Packages. Pack them first, or use project references while developing:

```sh
dotnet pack src/Companion.Launcher.Core -c Release -o packages
dotnet pack src/Companion.Launcher.Avalonia -c Release -o packages
# Windows Forms consumers also need Companion.Launcher.Windows.
# Android consumers also need Companion.Launcher.Android (Android workload required).
python scripts/new_game.py --id my-game --title "My Game" --platform desktop --output ../my-game-launcher
```

Choose `--platform windows`, `desktop` or `android`. The generator refuses existing
folders and produces a small adapter with pinned shared-package versions. Configure
the engine's supported options, asset validation, setup and native launch arguments.
The Android starter stays disabled until its actual game activity/assets are wired.
Porting a launcher does not automatically port a game engine or its ROM converter.

Common behavior includes:

- Boolean, integer, choice and text options with validation and Apply/Revert.
- Separate game, edition, profile and seed settings; stale-write detection.
- Desktop process launch with structured arguments; Android embedded-activity launch.
- Optional cancellable setup and quick-save resume, only when the game supplies them.
- Read-only achievement browsing with earned/locked indicators and counter progress.
- Compact touch navigation on handhelds and a familiar desktop layout.

[Adapter guide](ADAPTER-GUIDE.md) explains the integration boundary.
[Architecture](ARCHITECTURE.md) explains persistence and process lifetime.

## Achievements

Use [Companion Achievements](https://github.com/rages4calm/companion-achievements)
for the gameplay engine: a small C library with per-game JSON catalogs, counters,
milestones, transactional commits and notification callbacks. The launcher reads
its catalog/progress schema; it does not award achievements. No accounts or server.

## Test

```sh
dotnet run --project tests/Companion.Launcher.Tests -c Release
dotnet run --project tests/Companion.Launcher.Portable.Tests -c Release
python -m unittest discover -s tests -p test_generator.py
```

On Windows, `scripts/build.ps1` also checks the original Windows interface and
builds two independent package consumers. The portable probe renders desktop and
handheld layouts and tests actual settings, setup and process actions. CI runs
native checks on Windows, Linux and macOS, plus an Android build. These checks
are not a real-game playthrough or physical-device testing.

See [contributing](CONTRIBUTING.md), [changelog](CHANGELOG.md) and
[provenance](PROVENANCE.md). Copyright Carl Prewitt Jr. and contributors.
