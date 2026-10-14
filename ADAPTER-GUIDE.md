# Add a game once, reuse the shared launcher

Run `scripts/new_game.py` after `scripts/build.ps1`. The generated app consumes the
appropriate local packages with exact versions. The generated `nuget.config` points to this
folder's package feed; distribute the feed or configure a real package source on another
computer. Keep its `packages.lock.json` and use `dotnet restore --locked-mode` in CI.
The feed config also allows nuget.org for Microsoft framework packs if a newer installed
SDK needs the .NET 8 references. Source mapping keeps Companion packages on the local feed.

## 1. Identity and branding

Choose a stable lowercase ASCII game ID, title, profiles and edition IDs. IDs used for
folder names reject traversal, reserved devices and trailing dots. A seed's user-visible
phrase is separate from its canonical safe ID/hash. Never derive session identity from
a display title that can change or from a numeric option index.

Change `GameDefinition.Branding` to a `LauncherTheme` with hex colors. The shared shell
keeps the familiar Companion layout. Each game may declare its own supported sections.
Avoid exposing a graphics/audio/input option merely because another game had it.

## 2. Native settings

`ReadSettings` returns a dictionary of cloned `JsonElement`s and a revision string.
Defaults fill missing declared keys; unknown keys survive. `ValidateCustom` adds engine
constraints to shared type/range/choice validation. Apply is explicit, and an unapplied
draft cannot launch or switch profile. Revert reloads the authoritative source.

Use `JsonGameAdapterBase` for brand-new launcher-owned JSON. For an existing engine's
INI/binary settings, implement `IGameAdapter` or override the read/apply methods together.
Compare the expected revision, merge known fields while preserving engine-owned fields,
and commit atomically. Do not write defaults over an unreadable owner file or create a
new settings path that abandons released preferences.

Input bindings are options keyed by names such as `action.fast-forward`. The engine
adapter decides which actions and physical controls exist; no shared 21-action table or
hardcoded controller indices are assumed.

## 3. Launch and setup

`CheckReady` checks the chosen profile's executable and validated content. `BuildLaunchPlan`
returns an absolute executable, absolute working directory, argument list and optional
environment. Each argument is a separate string:

```csharp
return new LaunchPlan(exe, installRoot,
    new[] { "--assets", assetPack, "--session-dir", existingSaveSession });
```

Do not concatenate a shell command. Define only flags the actual engine implements.
`CanResume` requires a tested quick-save path; never pass a resume flag by assumption.

Set `HasSetup` and implement `SetupAsync` only for a real setup flow. Validate the user's
input, prepare a fresh staging directory, run adapter-owned tools with `SetupProcessRunner`,
validate all generated data, then commit. On cancellation/failure leave existing content
unchanged. Progress uses `OperationProgress(detail, fraction)` with fraction 0-1 or null.
This toolkit contains no generic ROM extractor, patch compiler or downloaded game content.

## 4. Achievements

Use the sibling CompanionAchievements toolkit for the gameplay core. Define a catalog and
generate C definitions with its `tools/catalog.py`. The native adapter emits confirmed wins,
story completions and other completed events. Save loading and battle entry must not award.
The game's writer commits before popup/sound; failed commits must preserve the old state.

Declare `HasAchievements` and implement the provider:

```csharp
public override AchievementSnapshot ReadAchievements(ProfileDescriptor profile) =>
    AchievementJsonReader.Read(catalogPath, progressPath,
        Definition.Id, profile.EditionId, profile.SeedId);
```

Schema 1 expects game/edition/seed identity, stable achievement IDs and Unix-second unlock
timestamps (zero while locked). The browser validates known states, ignores unknown IDs,
accepts a missing progress file as locked and never writes progress or triggers notifications.
The demo catalog is synthetic; each real game writes its own goals and titles.

EarthBound's existing achievement document is not assumed to have this schema. An EarthBound
adapter can translate its established records into `AchievementSnapshot` read-only rather
than rewriting earned dates, converting saves or moving profile paths.

## Existing projects: deliberate migration later

- EarthBound: native binary/INI settings, Redux assets and seed rules remain in its adapter.
- Zelda: native INI, soundtrack and networking/campaign rules remain in its adapter.
- Other games: native input/setup/state rules remain in their own adapters.

Those games were not edited or migrated by this extraction. A future migration should
first build a private launcher against copied fixtures, prove settings/save path parity,
then replace only the intended launcher after the owner requests that migration.

## Verification

`scripts/build.ps1` runs core/process, generator and real Windows interaction checks,
renders the demo, proves two consumers use pinned packages, and exports source/packages.
Use `-AchievementSdk "../CompanionAchievements"` after building that sibling's synthetic
host to also validate the native-written progress with the managed achievement reader.
The demo's `--smoke-test <fresh-dir>` renders every declared page, checks actual boolean
edits, draft survival, disabled Play, blocked profile switches and Revert without applying.
The Windows probe also tests Apply, actual Play/Resume processes, cancelled setup data
preservation and successful setup's settings refresh/readiness. Synthetic checks are
not a full-game or another-platform certification.
