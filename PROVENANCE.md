# Provenance

The Companion-specific UI and workflows were created for Carl Prewitt Jr.'s game projects.
This extraction follows the existing EarthBound Companion purple layout and the shared
patterns used in Zelda's custom Companion UI. It is a new generic library boundary, not
a copy of any game's engine, ROM hack, artwork or setup pipeline.

Read-only sources used to establish that boundary:

- EarthBound Companion `Companion/Program.cs`, `Settings.cs`, `GameLaunch.cs`,
  `HostRuntime.cs`, `AchievementProfiles.cs` and `Ports/UI/MainView.cs`.
- Zelda Companion `Companion/Theme.cs`, `MainForm.cs` and `Install.cs`.
- The separate CompanionAchievements native C library and schema-v1 tooling.

Zelda's older repaired RadzPrower launcher is a separate historical component of that
game project; it is not included in this new template. No third-party launcher GUI was
imported. Windows Forms and .NET are the platform frameworks used by this first host.

The demo's three catalog entries and static progress fixture are synthetic examples from
CompanionAchievements/examples/trail (MIT), included to verify interchange without game
content. SampleEngine is a newly written process fixture.

No existing game repository, installed launcher, save or asset was modified. No remote
was created and no package was published as part of this local extraction.
