using System.Reflection;
using System.Text.Json;
using Companion.Launcher.Core;

namespace Companion.Template;

public sealed class PortableDemoAdapter : JsonGameAdapterBase
{
    readonly bool mobile;
    readonly string fixtures;
    public static readonly GameDefinition Game = new("example-trail", "Companion Template", "A reusable launcher example. No game or ROM is included.", new OptionDefinition[] {
        new("display.integer", "Display", "Integer scaling", OptionKind.Boolean, OptionValue.From(true), "Example engine setting."),
        new("display.aspect", "Display", "Aspect ratio", OptionKind.Choice, OptionValue.From("16:9"), Choices: new[] { new ChoiceItem("4:3", "4:3"), new ChoiceItem("16:9", "16:9"), new ChoiceItem("21:9", "21:9") }),
        new("audio.volume", "Audio", "Music volume", OptionKind.Integer, OptionValue.From(80), Minimum: 0, Maximum: 100),
        new("player.label", "Game", "Player label", OptionKind.Text, OptionValue.From("Player")) }, HasSetup: true, HasAchievements: true, CanResume: true);

    public PortableDemoAdapter(string dataRoot, bool mobile = false) : base(Game, new[] { new ProfileDescriptor("original", "Original example", "original"), new ProfileDescriptor("challenge", "Challenge example", "challenge") }, dataRoot)
    {
        this.mobile = mobile;
        fixtures = Path.Combine(DataRoot, "synthetic-catalog");
        Directory.CreateDirectory(fixtures);
        foreach (var name in new[] { "catalog.json", "progress.json" })
        {
            var path = Path.Combine(fixtures, name);
            if (File.Exists(path)) continue;
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("PortableDemo.Shared.Data." + name) ?? throw new IOException("Missing synthetic catalog resource.");
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            resource.CopyTo(output);
        }
    }

    string Engine => Path.Combine(AppContext.BaseDirectory, "Engine", OperatingSystem.IsWindows() ? "SampleEngine.exe" : "SampleEngine");
    string Marker(ProfileDescriptor profile) => Path.Combine(ProfileDirectory(profile), "synthetic-ready.txt");
    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) =>
        !File.Exists(Marker(profile)) ? new(false, "Run the synthetic setup example to enable Play.") :
        !mobile && !File.Exists(Engine) ? new(false, "Build the sample engine first.") : new(true, "Ready to run the synthetic example.");

    public override async Task SetupAsync(ProfileDescriptor profile, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        for (var step = 0; step < 3; step++) { cancellationToken.ThrowIfCancellationRequested(); progress?.Report(new("Preparing synthetic example…", step / 3.0)); await Task.Delay(100, cancellationToken); }
        Directory.CreateDirectory(ProfileDirectory(profile));
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(Marker(profile), "This is synthetic demo data, not game assets.");
    }

    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume) =>
        new(mobile ? "embedded-demo" : Engine, ProfileDirectory(profile), new[] { "--receipt", Path.Combine(ProfileDirectory(profile), "last-run.json"), "--profile", profile.Id, resume ? "--resume" : "--new", "--label", values["player.label"].GetString()! });

    public override AchievementSnapshot ReadAchievements(ProfileDescriptor profile) => profile.EditionId == "original"
        ? AchievementJsonReader.Read(Path.Combine(fixtures, "catalog.json"), Path.Combine(fixtures, "progress.json"), Definition.Id, profile.EditionId)
        : new("Challenge example: no catalog supplied", Array.Empty<AchievementEntry>());
}
