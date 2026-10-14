using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Companion.Launcher.Avalonia;
using Companion.Launcher.Core;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        AppBuilder.Configure<ProbeApp>().UseSkia().WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var output = Path.GetFullPath(args.Length == 2 && args[0] == "--output" ? args[1] : Path.Combine("build", "portable-smoke", Guid.NewGuid().ToString("N")));
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        Directory.CreateDirectory(output);
        var adapter = new ProbeAdapter(Path.Combine(output, "data"));
        var controller = new LauncherController(adapter);
        var view = new CompanionView(controller, new DesktopLaunchService());
        var window = new Window { Width = 1120, Height = 800, Content = view };
        window.Show(); Pump();
        var checks = 0;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; }
        T Control<T>(string name) where T : Avalonia.Controls.Control => window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
        void Click(string name) { Control<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }

        Check(!Control<Button>("play").IsEnabled, "Play disabled before setup");
        view.Navigate("section:Display"); Pump();
        Control<CheckBox>("option:display.integer").IsChecked = false; Pump();
        view.Navigate("play"); Pump();
        Check(controller.HasChanges && !Control<Button>("play").IsEnabled, "draft survives navigation and blocks Play");
        Control<ComboBox>("profiles").SelectedIndex = 1; Pump();
        Check(controller.Profile.Id == "original" && Control<ComboBox>("profiles").SelectedIndex == 0, "dirty profile switch rejected without draft loss");
        Click("revert"); Check(!controller.HasChanges && controller.Snapshot.Values["display.integer"].GetBoolean(), "Revert restores saved/default values");
        view.Navigate("section:Audio"); Pump(); Control<NumericUpDown>("option:volume").Value = null;
        view.Navigate("section:Display"); Pump(); Control<CheckBox>("option:display.integer").IsChecked = false; Pump();
        Check(!Control<Button>("apply").IsEnabled && !view.CanClose, "editing another field cannot hide invalid input"); Click("revert");
        view.Navigate("section:Display"); Pump();
        Control<CheckBox>("option:display.integer").IsChecked = false; Pump(); Click("apply");
        Check(!controller.HasChanges && !new LauncherController(adapter).Snapshot.Values["display.integer"].GetBoolean(), "Apply persisted through a new controller");
        var other = new LauncherController(adapter);
        Control<CheckBox>("option:display.integer").IsChecked = true;
        other.SetOption("volume", OptionValue.From(22)); other.Apply();
        Click("apply");
        Check(controller.HasChanges && Control<TextBlock>("status").Text!.Contains("outside"), "external writer conflict preserves draft");
        Click("revert"); Check(controller.Snapshot.Values["volume"].GetInt32() == 22, "Revert reloads external winner");

        view.Navigate("setup"); Pump(); Click("setup-start");
        Check(view.IsBusy && !Control<ComboBox>("profiles").IsEnabled, "setup locks profiles");
        Click("setup-cancel"); Until(() => !view.IsBusy);
        Check(!File.Exists(adapter.Marker) && controller.Snapshot.Values["volume"].GetInt32() == 22, "cancel leaves authoritative data intact");
        adapter.FastSetup = true; Click("setup-start"); Until(() => !view.IsBusy);
        Check(File.Exists(adapter.Marker) && controller.Snapshot.Values["volume"].GetInt32() == 42, "successful setup refreshes native settings");
        view.Navigate("play"); Pump(); Check(Control<Button>("play").IsEnabled, "setup refreshes readiness");
        Click("play"); Until(() => !view.IsBusy);
        using (var receipt = JsonDocument.Parse(File.ReadAllText(adapter.Receipt)))
            Check(receipt.RootElement.GetProperty("arguments").EnumerateArray().Any(value => value.GetString() == "--new"), "Play runs actual process with structured arguments");
        Click("resume"); Until(() => !view.IsBusy);
        using (var receipt = JsonDocument.Parse(File.ReadAllText(adapter.Receipt)))
            Check(receipt.RootElement.GetProperty("arguments").EnumerateArray().Any(value => value.GetString() == "--resume"), "Resume reaches actual process");
        view.Navigate("achievements"); Pump();
        Check(window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "1 of 3 earned"), "achievement browser uses earned/partial/locked shared data");
        using (var image = window.CaptureRenderedFrame() ?? throw new Exception("No pixels rendered.")) image.Save(Path.Combine(output, "desktop-achievements.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        window.Width = 390; window.Height = 760; Pump();
        Check(Control<ComboBox>("navigation").IsVisible, "compact touch navigation shown");
        Check(Control<ComboBox>("navigation").SelectedIndex == view.PageIds.ToList().IndexOf("achievements"), "compact navigation tracks the displayed page");
        using (var image = window.CaptureRenderedFrame() ?? throw new Exception("No compact pixels rendered.")) image.Save(Path.Combine(output, "mobile-achievements.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        view.Navigate("section:Display"); Pump();
        using (var image = window.CaptureRenderedFrame()!) image.Save(Path.Combine(output, "mobile-options.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
        Control<CheckBox>("option:display.integer").IsChecked = true; Pump();
        Check(!view.CanClose, "closing cannot silently lose a draft"); Click("revert"); Check(view.CanClose, "reverted view can close");
        window.Close();
        var result = new { passed = true, checks, platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription, output, syntheticOnly = true };
        File.WriteAllText(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(result)); return 0;
    }
    static void Pump() => Dispatcher.UIThread.RunJobs();
    static void Until(Func<bool> condition)
    {
        var time = Stopwatch.StartNew();
        while (!condition()) { if (time.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Portable UI action did not finish."); Pump(); Thread.Sleep(10); }
        Pump();
    }
}

public sealed class ProbeApp : Application
{
    public override void Initialize() { RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
}

sealed class ProbeAdapter(string root) : JsonGameAdapterBase(
    new GameDefinition("example-trail", "Companion Template", "Synthetic portable probe", new[] {
        new OptionDefinition("display.integer", "Display", "Integer scaling", OptionKind.Boolean, OptionValue.From(true)),
        new OptionDefinition("volume", "Audio", "Music volume", OptionKind.Integer, OptionValue.From(80), Minimum: 0, Maximum: 100) }, HasSetup: true, HasAchievements: true, CanResume: true),
    new[] { new ProfileDescriptor("original", "Original", "original"), new ProfileDescriptor("challenge", "Challenge", "challenge") }, root)
{
    public bool FastSetup { get; set; }
    public string Marker => Path.Combine(ProfileDirectory(Profiles[0]), "ready.txt");
    public string Receipt => Path.Combine(ProfileDirectory(Profiles[0]), "process.json");
    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) => new(File.Exists(Marker), File.Exists(Marker) ? "Ready." : "Run setup.");
    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume) => new(
        Path.Combine(AppContext.BaseDirectory, "Engine", OperatingSystem.IsWindows() ? "SampleEngine.exe" : "SampleEngine"), ProfileDirectory(profile), new[] { "--receipt", Receipt, resume ? "--resume" : "--new" });
    public override async Task SetupAsync(ProfileDescriptor profile, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        await Task.Delay(FastSetup ? 20 : 5000, cancellationToken);
        var state = ReadSettings(profile);
        state.Values["volume"] = OptionValue.From(42);
        foreach (var option in Definition.Options) state.Values.TryAdd(option.Id, option.DefaultValue.Clone());
        ApplySettings(profile, state.Values, state.Revision);
        File.WriteAllText(Marker, "synthetic-ready");
    }
    public override AchievementSnapshot ReadAchievements(ProfileDescriptor profile) => AchievementJsonReader.Read(
        Path.Combine(AppContext.BaseDirectory, "Data", "catalog.json"), Path.Combine(AppContext.BaseDirectory, "Data", "progress.json"), "example-trail", "original");
}
