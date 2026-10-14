# Shared code and game boundaries

```
Each game's small adapter + branding + optional achievement provider
                              |
              Companion.Launcher.Windows 0.1.0
              (familiar Companion UI; declared pages)
                              |
                 Companion.Launcher.Core 0.1.0
                 (contracts, drafts, JSON, processes)
                              |
            Game's own native engine / setup tools / data

Game gameplay events --> CompanionAchievements C core --> transactional game writer
                                      |
                           catalog/progress schema v1
                                      |
                      read-only launcher achievement browser
```

## Shared launcher

- Descriptor-driven options: Boolean, Integer, Choice and Text, grouped into sections.
- Configurable palette, title and description; no embedded game artwork.
- Draft/Apply/Revert, validation, stale-write conflict detection and unknown JSON-key preservation.
- Stable game/profile/edition/seed namespaces. Null seed and a seed literally named `unseeded` differ.
- Readiness and declared optional setup/achievements/quick-save resume capabilities.
- Structured executable/cwd/argument/environment plans, process status, explicit setup cancellation.
- Read-only achievement catalog/progress validation, locked/earned/progress presentation.
- Windows page rendering and draft safety smoke checks.

The core does not depend on WinForms, Avalonia, SDL, a ROM, an emulator or an engine.
The Windows shell is a replaceable host. The `ProcessGameRuntime` helper is appropriate
for desktop processes; Android JNI launch requires a different platform host.

## Adapter owns the game's behavior

The adapter defines supported settings and maps them to the native format. It validates
ROM/assets, performs staged setup, chooses exact native flags/environment, preserves
released save paths and supplies achievement locations. It does not assume all games
use the same input actions, widescreen implementation, binary saves or engine settings.

`JsonGameAdapterBase` is for a new integration where launcher JSON is authoritative.
For an existing engine-owned file, implement `IGameAdapter` and read/merge that format.
`ApplySettings` owns the whole write operation; the core never writes a second settings
file before or after an adapter that could leave two sources out of sync.

## Persistence guarantees and limits

The default JSON writer holds a cooperative per-file lock, compares the revision of the
file that was read, flushes a same-directory temporary file and atomically replaces the
destination. Failed validation/conflicts leave the file and draft unchanged. It creates
no recovery archives. A native writer that ignores this lock can still race the final
revision check; adapters for such engines must apply that engine's own synchronization.

Settings namespace for new adapters:
`<data-root>/<game-id>/<edition-id>/<profile-id>/unseeded/settings.json`, or
`.../<profile-id>/seeded/<seed-id>/settings.json`.
Achievement providers use their own tested progress locations; matching IDs are checked
inside JSON, so the browser does not assume the settings folder equals the progress folder.

Closing the launcher only disposes process handles. It does not kill the game. Cancelling
an adapter-owned setup process stops that child tree, drains output and reports cancellation;
staging/validation/commit remain the adapter's responsibility.

## Versions

Core and Windows packages are initially pinned to 0.1.0. Consumers include exact-version
NuGet references and a restore lock file. Fixes are implemented here, tested with consumers,
packed as a new version and deliberately adopted. Do not silently overwrite an installed
game's launcher merely because a shared package changed.

CompanionAchievements toolkit 0.2.0 / C ABI 0.1.0 / JSON schema 1 are separate version axes.
Stable achievement IDs survive a catalog revision; unknown progress IDs are ignored by
older browsers and are never deleted by the launcher.
