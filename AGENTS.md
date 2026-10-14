# Companion Launcher shared foundation

This directory is a separate local source project. It must not modify installed games,
their saves/settings, or sibling projects as part of builds, smoke tests or generation.
Secret of the Stars remains paused until its owner explicitly resumes that project.

Keep the core independent of GUI and engine dependencies. Game setup, ROM validation,
native binary settings, input bindings, asset formats and save compatibility belong to
the game adapter. Declare a UI control only when the engine actually implements it.

The source generator refuses existing output directories. Generated apps consume pinned
packages; do not copy the shared shell per game. Update package versions and test consumers
when changing a contract. Preserve existing released namespaces during optional migration.

Use fresh synthetic data for tests. Do not package ROMs, private asset packs, music,
save files, states, personal settings or screenshots from real gameplay. Source exports
have an explicit source-file manifest. No new remotes or publishing without owner request.

Run scripts/build.ps1 after relevant changes, plus UI smoke rendering for shell changes.
The example engine is a process fixture, not a game or a gameplay certification.
