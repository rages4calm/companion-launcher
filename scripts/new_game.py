#!/usr/bin/env python3
"""Create a small game adapter that consumes pinned Companion Launcher packages."""
from __future__ import annotations

import argparse
import html
import json
import re
from pathlib import Path

VERSION = "0.2.0"
ROOT = Path(__file__).resolve().parents[1]


def validate_id(value: str) -> str:
    if not re.fullmatch(r"[a-z0-9][a-z0-9._-]{0,79}", value) or value.endswith("."):
        raise ValueError("game ID must be 1-80 lowercase ID characters, starting with a letter or digit")
    stem = value.split(".", 1)[0]
    if stem in {"con", "prn", "aux", "nul"} | {f"{prefix}{i}" for prefix in ("com", "lpt") for i in range(1, 10)}:
        raise ValueError("game ID must not be a reserved device name")
    return value


def create(game_id: str, title: str, output: Path, feed: Path, platform: str = "windows") -> Path:
    validate_id(game_id)
    if not title.strip() or len(title) > 160 or any(ord(c) < 32 or 0xD800 <= ord(c) <= 0xDFFF for c in title):
        raise ValueError("title must contain 1-160 printable characters")
    output, feed = output.resolve(), feed.resolve()
    if output.exists():
        raise ValueError(f"output already exists; no files changed: {output}")
    if platform not in ("windows", "desktop", "android"):
        raise ValueError("platform must be windows, desktop or android")
    required = ["Companion.Launcher.Core", "Companion.Launcher.Windows" if platform == "windows" else "Companion.Launcher.Avalonia"]
    if platform == "android": required.append("Companion.Launcher.Android")
    for package in required:
        if not (feed / f"{package}.{VERSION}.nupkg").is_file():
            raise ValueError(f"missing pinned package {package} {VERSION}; run scripts/build.ps1 first")
    qid, qtitle = json.dumps(game_id), json.dumps(title, ensure_ascii=True)
    files = {
        "Game.Companion.csproj": f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>{html.escape(game_id)}.Companion</AssemblyName>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Companion.Launcher.Core" Version="[{VERSION}]" />
    <PackageReference Include="Companion.Launcher.Windows" Version="[{VERSION}]" />
  </ItemGroup>
</Project>
''',
        "nuget.config": f'''<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear/>
    <add key="CompanionLocal" value="{html.escape(str(feed), quote=True)}" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="CompanionLocal"><package pattern="Companion.Launcher.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
''',
        "Program.cs": '''using Companion.Launcher.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => CompanionApplication.Run(new GameAdapter(), args);
}
''',
        "GameAdapter.cs": f'''using System.Text.Json;
using Companion.Launcher.Core;

// This is the only game-specific layer. Declare settings only when the engine implements them.
public sealed class GameAdapter : JsonGameAdapterBase
{{
    private static readonly GameDefinition Game = new(
        {qid}, {qtitle}, "Choose your native game executable to begin.",
        new[] {{ new OptionDefinition("engine.path", "Game", "Native executable", OptionKind.Text,
            OptionValue.From(Path.Combine(AppContext.BaseDirectory, "Game", "Game.exe")),
            "An existing native executable. ROM conversion and engine settings belong in this adapter.") }});

    public GameAdapter(string? dataRoot = null) : base(Game, new[] {{ new ProfileDescriptor("original", "Original", "original") }}, dataRoot ??
        Environment.GetEnvironmentVariable("COMPANION_DATA_ROOT") ??
        CompanionDataPaths.DesktopRoot()) {{ }}

    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values)
    {{
        var executable = values["engine.path"].GetString() ?? "";
        return Path.IsPathFullyQualified(executable) && File.Exists(executable)
            ? new(true, "Ready to play.") : new(false, "Enter the full path to your native executable, then Apply.");
    }}

    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume)
    {{
        var executable = values["engine.path"].GetString()!;
        // Add structured native arguments here. Do not build a shell command string.
        return new(executable, Path.GetDirectoryName(executable)!, Array.Empty<string>());
    }}

    // Optional: declare HasSetup/HasAchievements/CanResume and implement those adapter methods.
    // Existing games must preserve their released save/settings paths instead of adopting new ones blindly.
}}
''',
        "README.md": f'''# {title} Companion

Generated from Companion Launcher {VERSION}. Shared implementation lives in the packages;
`GameAdapter.cs` supplies this game's behavior. No engine, ROM or game assets are included.

Build with `dotnet build -c Release`, then run the executable in `bin/Release/net8.0-windows`.
Use `--smoke-test <fresh-folder>` to render the declared pages without starting a game.

Set the native executable path under Game and Apply. This starter launches it with no arguments.
Before real integration, implement the engine's native settings translation, data/session arguments,
asset readiness and optional setup. Do not add controls for features the engine does not implement.

The local Companion packages are pinned to {VERSION} in `nuget.config`; nuget.org supplies
Microsoft framework packs if the installed SDK needs them. Keep `packages.lock.json`
after the first restore; use `dotnet restore --locked-mode` in CI. Distribute that package version
or configure your own feed for another machine; the generated project does not copy the shared source.

New settings go under `CompanionGames/{game_id}/original/original/unseeded` in local application data.
`COMPANION_DATA_ROOT` can select an isolated test root. Native saves remain the adapter's responsibility.

For achievements, use the sibling CompanionAchievements SDK, define a game-specific catalog,
and emit completed gameplay events. The launcher reads progress; it never awards achievements.
''',
        ".gitignore": "bin/\nobj/\n.vs/\n*.user\n",
    }
    if platform != "windows":
        package_refs = '\n'.join(f'    <PackageReference Include="{name}" Version="[{VERSION}]" />' for name in required)
        external = "Avalonia.Android" if platform == "android" else "Avalonia.Desktop"
        android_properties = f'<SupportedOSPlatformVersion>26.0</SupportedOSPlatformVersion><ApplicationId>org.companion.game_{re.sub("[^a-z0-9]", "_", game_id)}</ApplicationId><ApplicationTitle>{html.escape(title)}</ApplicationTitle><AndroidPackageFormats>apk</AndroidPackageFormats><PublishTrimmed>false</PublishTrimmed><RunAOTCompilation>false</RunAOTCompilation><AndroidEnableMarshalMethods>false</AndroidEnableMarshalMethods><RuntimeIdentifier Condition="\'$(RuntimeIdentifier)\'==\'\'">android-arm64</RuntimeIdentifier>' if platform == "android" else ""
        files["Game.Companion.csproj"] = f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>{"Exe" if platform == "android" else "WinExe"}</OutputType><TargetFramework>{"net10.0-android" if platform == "android" else "net8.0"}</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><AssemblyName>{game_id}.Companion</AssemblyName>{android_properties}</PropertyGroup>
  <ItemGroup>
{package_refs}
    <PackageReference Include="{external}" Version="12.1.4" />
  </ItemGroup>
</Project>
'''
        files["README.md"] += f'\nPlatform: **{platform}**. This project consumes the shared Avalonia view.\n'
        if platform == "desktop":
            files["Program.cs"] = '''using Avalonia;
using Companion.Launcher.Avalonia;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        CompanionApp.AdapterFactory = () => new GameAdapter();
        AppBuilder.Configure<CompanionApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
'''
        else:
            del files["Program.cs"]
            files["GameAdapter.cs"] = f'''using System.Text.Json;
using Companion.Launcher.Core;

public sealed class GameAdapter(string dataRoot) : JsonGameAdapterBase(
    new GameDefinition({qid}, {qtitle}, "Connect this game's Android engine and assets in GameAdapter.cs.", Array.Empty<OptionDefinition>()),
    new[] {{ new ProfileDescriptor("original", "Original", "original") }}, dataRoot)
{{
    // Validate this game's private assets here. No desktop executables or ROMs are bundled.
    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) => new(false, "Android game integration has not been supplied yet.");
    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume) =>
        new("game", ProfileDirectory(profile), Array.Empty<string>());
}}
'''
            files["MainActivity.cs"] = '''using Android.App;
using Android.Content;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Companion.Launcher.Avalonia;
using Companion.Launcher.Android;

[Activity(MainLauncher=true, Exported=true, Theme="@style/CompanionTheme", ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    AndroidActivityLaunchService? launch;
    protected override void OnCreate(Android.OS.Bundle? state)
    {
        launch = new(this, typeof(GameActivity), "game");
        CompanionApp.AdapterFactory = () => new GameAdapter(Path.Combine(FilesDir!.AbsolutePath, "GameData"));
        CompanionApp.LaunchServiceFactory = () => launch;
        base.OnCreate(state);
    }
    protected override void OnActivityResult(int code, Result result, Intent? data)
    {
        if (launch?.OnActivityResult(code, result) != true) base.OnActivityResult(code, result, data);
    }
}

[Android.App.Application]
public sealed class AndroidApp(nint reference, Android.Runtime.JniHandleOwnership ownership) : AvaloniaAndroidApplication<CompanionApp>(reference, ownership)
{
    public override void OnCreate() => base.OnCreate();
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}

// Replace this placeholder with this game's embedded SDL/native activity.
[Activity(Exported=false, Theme="@style/CompanionTheme")]
public sealed class GameActivity : Activity
{
    protected override void OnCreate(Android.OS.Bundle? state)
    {
        base.OnCreate(state);
        SetContentView(new Android.Widget.TextView(this) { Text = "Supply the native Android game activity for this project." });
    }
}
'''
            files["Resources/values/styles.xml"] = '<?xml version="1.0" encoding="utf-8"?><resources><style name="CompanionTheme" parent="Theme.AppCompat.NoActionBar" /></resources>\n'
            files["Properties/AndroidManifest.xml"] = '<?xml version="1.0" encoding="utf-8"?><manifest xmlns:android="http://schemas.android.com/apk/res/android"><application android:allowBackup="false" /></manifest>\n'
            files["README.md"] += '\nRequires .NET 10 Android workload, Android SDK and JDK 21. Build with `dotnet build -c Release`. Readiness stays disabled until you integrate the native Android engine/activity and validated game assets. Paths are app-private; use an Android document picker for user ROM imports rather than requesting broad storage access.\n'
    if platform != "windows":
        files["README.md"] = f"""# {title} Companion

Generated with Companion Launcher {VERSION}, using the **{platform}** host.
Shared implementation comes from pinned packages; GameAdapter.cs supplies the game's rules.
No engine, game assets or ROM converter is bundled.

Build with `dotnet build -c Release`. Desktop requires .NET 8; Android requires .NET 10,
the Android workload, Android SDK and JDK 21. SDK paths can be passed as MSBuild properties.
The local feed is in nuget.config; nuget.org supplies framework/Avalonia dependencies.
Keep packages.lock.json and use locked restore in CI.

Desktop: configure the native executable, then Apply. Add actual engine settings/session
arguments in GameAdapter.cs. Android: replace the placeholder Activity and validate
app-private game assets; readiness deliberately stays disabled until that integration exists.
Use Android's document picker for imports, not broad filesystem permissions.

Achievements are optional: define your game's catalog with CompanionAchievements, connect
completed gameplay events, then supply a read-only provider to the launcher. Preserve
released save/settings namespaces when migrating an existing game.
"""
    output.mkdir(parents=True, exist_ok=False)
    for relative, contents in files.items():
        (output / relative).parent.mkdir(parents=True, exist_ok=True)
        (output / relative).write_text(contents, encoding="utf-8", newline="\n")
    return output


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--id", required=True, help="stable lowercase game ID")
    parser.add_argument("--title", required=True)
    parser.add_argument("--output", required=True, type=Path, help="new directory only")
    parser.add_argument("--feed", type=Path, default=ROOT / "packages")
    parser.add_argument("--platform", choices=("windows", "desktop", "android"), default="windows")
    args = parser.parse_args()
    try:
        result = create(args.id, args.title, args.output, args.feed, args.platform)
    except (ValueError, OSError) as error:
        parser.exit(1, str(error) + "\n")
    print(json.dumps({"created": str(result), "launcherVersion": VERSION, "gameId": args.id}))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
