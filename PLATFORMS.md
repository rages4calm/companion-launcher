# Platform integration and verification

The Windows Forms shell remains separate; its control/layout code was not replaced
by the portable host. Core additions are additive. The Windows regression suite
checks real Apply/Play/Resume/setup controls and synthetic native child processes.

The Avalonia 12.1.4 host shares the same core and is used by Linux, macOS and
Android. Desktop uses `DesktopLaunchService`. Android injects
`AndroidActivityLaunchService`, which launches only the in-package activity and
route registered by the host. It forwards arguments/session directory through
explicit Intent extras, rejects process environment overrides, and waits for
OnActivityResult. It never attempts to execute a Windows/Linux binary on Android.

## Building

Desktop examples use .NET 8. Publish on the corresponding platform, e.g.
`dotnet publish samples/Companion.Desktop -c Release -r linux-x64 --self-contained true`.
Other RIDs include linux-arm64, osx-x64 and osx-arm64. macOS application bundling,
signing and notarization are separate distribution work; an unsigned developer
build should not be presented as a notarized App Store application.

Android uses .NET 10, JDK 21 and Android SDK 36.1. Use android-arm64 for handhelds,
android-x64 for an emulator. Pass AndroidSdkDirectory/JavaSdkDirectory if discovery
is not configured. No broad storage permission is requested. ROM conversion,
Storage Access Framework file picking and native engine libraries belong to the
game integration; no engine or general ROM converter is part of this SDK.

The portable host uses Avalonia 12.1.4 and its current native graphics dependencies.
Android 16-KiB page compatibility is checked with the build/emulator; an older
2.88.9 Skia dependency was rejected during testing because Android showed a
compatibility warning. The Windows Forms host has no Avalonia dependency.
The Android sample/starter disable AndroidEnableMarshalMethods to avoid the
.NET 10 Release JNI callback registration failure with untrimmed assemblies.

## Data and lifecycle

New desktop integrations use LocalApplicationData on Windows,
XDG_DATA_HOME (or ~/.local/share) on Linux, and ~/Library/Application Support on
macOS. Android passes its own app-private FilesDir. Existing integrations must
retain their released save/settings namespaces through their own adapter.

Compact navigation replaces the rail below 700 logical pixels. Controls have touch
sized actions and vertically scrollable content. Unsaved/invalid edits prevent
silent desktop close and profile switching. Setup can be cancelled; closing a
launcher does not kill a desktop game. Android game activity cancellation/Back
returns to the launcher and is not treated as an engine failure.

## Evidence and limits

Portable tests exercise actual UI callbacks, settings conflicts, profile guards,
invalid numeric input, cancelled and successful setup, actual desktop Play/Resume,
achievement browsing and rendered desktop/handheld layouts. GitHub Actions runs
these on Windows, Linux and macOS; the Android job compiles an ARM64 APK. Local
emulator evidence covers the synthetic activity flow. Physical Android handhelds,
all GPU/driver combinations, macOS signing and integrations into real games require
the consuming project's own testing. No existing game was migrated here.

Official framework references:
- https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform
- https://docs.avaloniaui.net/docs/app-development/cross-platform-solution-setup
