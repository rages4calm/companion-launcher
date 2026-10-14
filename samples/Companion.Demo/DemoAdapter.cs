using System.Text.Json;
using Companion.Launcher.Core;

namespace Companion.Demo;

public sealed class DemoAdapter : JsonGameAdapterBase
{
    private static readonly GameDefinition Game = new(
        "example-trail", "Companion Template", "A synthetic example for future games. No ROM or game assets are included.",
        new OptionDefinition[] {
            new("display.aspect", "Display", "Aspect ratio", OptionKind.Choice, OptionValue.From("16:9"), "Each engine decides which display options it supports.", Choices: new[] { new ChoiceItem("4:3", "4:3"), new ChoiceItem("16:9", "16:9"), new ChoiceItem("21:9", "21:9") }),
            new("display.integer", "Display", "Integer scaling", OptionKind.Boolean, OptionValue.From(true), "Example setting passed to the synthetic engine."),
            new("audio.volume", "Audio", "Music volume", OptionKind.Integer, OptionValue.From(80), "Range 0-100.", 0, 100),
            new("controls.fast-forward", "Controls", "Fast-forward action", OptionKind.Choice, OptionValue.From("hold"), "Bindings use stable action names; every game supplies its own actions.", Choices: new[] { new ChoiceItem("hold", "Hold"), new ChoiceItem("toggle", "Toggle") }),
            new("player.label", "Game", "Player label", OptionKind.Text, OptionValue.From("Player"), "A harmless text option used by the process fixture.") },
        HasAchievements: true);

    public DemoAdapter() : base(Game, new[] {
        new ProfileDescriptor("original", "Original example", "original"),
        new ProfileDescriptor("challenge", "Challenge example", "challenge"),
        new ProfileDescriptor("seed-example", "Seeded example", "original", "demo-42") },
        System.Environment.GetEnvironmentVariable("COMPANION_DEMO_DATA") ?? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "CompanionTemplateDemo")) { }

    private static string Engine => Path.Combine(AppContext.BaseDirectory, "Engine", "SampleEngine.exe");

    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) =>
        File.Exists(Engine) ? new(true, "Ready to run the synthetic process example.") : new(false, "Build the demo to supply its synthetic process fixture.");

    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume) =>
        new(Engine, Path.GetDirectoryName(Engine)!, new[] {
            "--receipt", Path.Combine(ProfileDirectory(profile), "last-run.json"),
            "--profile", profile.Id, "--settings", Path.Combine(ProfileDirectory(profile), "settings.json"),
            "--aspect", values["display.aspect"].GetString()!, "--label", values["player.label"].GetString()! });

    public override AchievementSnapshot ReadAchievements(ProfileDescriptor profile)
    {
        if (profile.EditionId != "original") return new("Challenge example: no catalog supplied", Array.Empty<AchievementEntry>());
        var catalog = Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json");
        var progress = profile.SeedId is null ? Path.Combine(AppContext.BaseDirectory, "Data", "progress.json") : null;
        return AchievementJsonReader.Read(catalog, progress, Definition.Id, profile.EditionId, profile.SeedId);
    }
}
