# Contributing

Issues and pull requests are welcome. Include OS/architecture, SDK version,
launcher version, a small synthetic reproduction and relevant logs. Do not upload
ROMs, extracted game content, saves, copyrighted music or personal settings.

Keep game-specific formats, asset validation and engine flags in adapters. New
shared controls should represent an implemented engine feature, not a promise.
Preserve the existing Windows Forms shell and its regression tests. Portable UI
changes need the portable interaction/rendering probe as well as a desktop build.
Android changes need an APK build and, where possible, an emulator/device check.

Run the commands in README. Commit source and focused tests; bin/obj/build/packages
and dist are generated and ignored. Add no mandatory accounts, networking or cloud
services. Keep schemas and stable achievement IDs compatible. Shared package
updates are explicit, tested version changes; do not silently replace another
game's launcher or move its user data.

The project is MIT licensed. Retain notices for any dependency or contributed code.
