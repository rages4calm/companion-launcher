using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Companion.Launcher.Core;
using Companion.Launcher.Windows;

namespace Companion.Launcher.Windows.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var taskId = Guid.NewGuid().ToString("N");
        var outputIndex = Array.IndexOf(args, "--output");
        var output = Path.GetFullPath(outputIndex >= 0 && outputIndex + 1 < args.Length ? args[outputIndex + 1] :
            Path.Combine(Path.GetTempPath(), "companion-windows-report-" + taskId));
        var data = Path.Combine(Path.GetTempPath(), "companion-windows-data-" + taskId);
        var results = new List<object>();
        try
        {
            if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be a fresh directory.");
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(data);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var adapter = new ProbeAdapter(data, Path.Combine(AppContext.BaseDirectory, "Engine", "SampleEngine.exe"));
            var controller = new LauncherController(adapter);
            using var form = new LauncherForm(controller)
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000)
            };
            form.Show();
            Application.DoEvents();

            Navigation(form, "Display").PerformClick();
            var input = Descendants(form).OfType<CheckBox>().Single(control => control.AccessibleName == "Integer scaling");
            input.Checked = false;
            Require(controller.HasChanges, "The real checkbox did not create a draft.");
            Action(form, "Apply settings").PerformClick();
            Require(!controller.HasChanges && adapter.ApplyCalls == 1, "The real Apply button did not commit its draft.");
            var persisted = adapter.ReadSettings(controller.Profile);
            Require(persisted.Values["display.integer"].ValueKind == JsonValueKind.False, "The applied option was not persisted.");
            results.Add(new { name = "apply-button-persists", passed = true, applyCalls = adapter.ApplyCalls });

            Navigation(form, "Play").PerformClick();
            var beforePlans = adapter.PlanCalls.Count;
            Action(form, "Play").PerformClick();
            PumpUntil(() => File.Exists(adapter.NormalReceipt), "The synthetic Play process did not create its receipt.");
            PumpUntil(() => Action(form, "Play").Enabled && VisibleStatus(form, "Game closed"), "The exited process did not release Play and update status.");
            Require(adapter.PlanCalls.Count == beforePlans + 1 && adapter.PlanCalls[^1] == false, "Play did not request a normal launch.");
            CheckReceipt(adapter.NormalReceipt, "normal");
            results.Add(new { name = "play-button-process-and-exit", passed = true, normalReceipt = adapter.NormalReceipt });

            Action(form, "Resume quick save").PerformClick();
            PumpUntil(() => File.Exists(adapter.ResumeReceipt), "The synthetic Resume process did not create its receipt.");
            PumpUntil(() => Action(form, "Play").Enabled && VisibleStatus(form, "Game closed"), "Resume did not return to the launcher after exit.");
            Require(adapter.PlanCalls[^1], "Resume did not pass true to the game adapter.");
            CheckReceipt(adapter.ResumeReceipt, "resume");
            results.Add(new { name = "resume-button-passes-true", passed = true, resumeReceipt = adapter.ResumeReceipt });

            var markerBefore = Hash(adapter.AuthoritativeMarker);
            var settingsBefore = Hash(adapter.SettingsPath(controller.Profile));
            adapter.CancelSetup = true;
            Navigation(form, "Game setup").PerformClick();
            Action(form, "Run setup").PerformClick();
            PumpUntil(() => adapter.SetupStarted == 1 && HasAction(form, "Cancel setup"), "The setup callback did not start.");
            Action(form, "Cancel setup").PerformClick();
            PumpUntil(() => adapter.SetupCancelled == 1 && HasAction(form, "Run setup") && Action(form, "Run setup").Enabled,
                "The setup cancellation did not restore the launcher.");
            Require(Hash(adapter.AuthoritativeMarker) == markerBefore && Hash(adapter.SettingsPath(controller.Profile)) == settingsBefore,
                "Cancellation changed authoritative data or applied settings.");
            Require(VisibleStatus(form, "Setup cancelled"), "Setup cancellation did not display its result.");
            results.Add(new { name = "setup-cancel-preserves-authoritative-data", passed = true, markerBefore, settingsBefore });

            adapter.CancelSetup = false;
            adapter.Ready = false;
            Navigation(form, "Play").PerformClick();
            Require(!Action(form, "Play").Enabled, "Play was enabled before setup made the profile ready.");
            Navigation(form, "Game setup").PerformClick();
            Action(form, "Run setup").PerformClick();
            PumpUntil(() => adapter.SetupSucceeded == 1 && HasAction(form, "Run setup") && Action(form, "Run setup").Enabled,
                "Successful setup did not finish and update the page.");
            Require(controller.GetReadiness().Ready, "Successful setup did not make game data ready.");
            Require(VisibleStatus(form, "Synthetic game data ready"), "Setup did not refresh the visible readiness label.");
            Navigation(form, "Play").PerformClick();
            Require(Action(form, "Play").Enabled, "Successful setup did not enable Play.");
            var setupSettings = adapter.ReadSettings(controller.Profile);
            Require(setupSettings.Values["display.integer"].ValueKind == JsonValueKind.True, "The synthetic setup did not supply its authoritative setting.");
            Require(controller.Snapshot.Values["display.integer"].ValueKind == JsonValueKind.True && controller.Snapshot.Revision == setupSettings.Revision,
                "The launcher did not reload authoritative settings after setup.");
            Require(!controller.HasChanges && adapter.ApplyCalls == 1, "Setup performed an extra UI Apply or retained a stale draft.");
            results.Add(new { name = "setup-success-updates-readiness", passed = true, setupCalls = adapter.SetupStarted });

            form.Hide();
            var report = new { passed = true, cases = results.Count, results, dataRoot = data, noExistingGameData = true };
            File.WriteAllText(Path.Combine(output, "integration.json"), JsonSerializer.Serialize(report, JsonOptions));
            Console.WriteLine(JsonSerializer.Serialize(new { passed = true, cases = results.Count, report = Path.Combine(output, "integration.json") }));
            return 0;
        }
        catch (Exception error)
        {
            if (Directory.Exists(output)) File.WriteAllText(Path.Combine(output, "integration.json"), JsonSerializer.Serialize(new { passed = false, results, error = error.ToString(), dataRoot = data }, JsonOptions));
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static Button Navigation(Form form, string text) => Descendants(form).OfType<Button>().Single(button => button.AccessibleName == text);
    private static Button Action(Form form, string text) => Descendants(form).OfType<Button>().Single(button => button.Text == text && button.AccessibleName is null);
    private static bool HasAction(Form form, string text) => Descendants(form).OfType<Button>().Any(button => button.Text == text && button.AccessibleName is null);
    private static bool VisibleStatus(Form form, string text) => Descendants(form).OfType<Label>().Any(label => label.Visible && label.Text.Contains(text, StringComparison.OrdinalIgnoreCase));
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void PumpUntil(Func<bool> condition, string failure)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(12)) throw new TimeoutException(failure);
            Application.DoEvents();
            Thread.Sleep(10);
        }
        Application.DoEvents();
    }

    private static void CheckReceipt(string path, string launchKind)
    {
        using var receipt = JsonDocument.Parse(File.ReadAllText(path));
        var arguments = receipt.RootElement.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()).ToArray();
        Require(arguments.Contains(launchKind), "The process did not receive the requested launch kind.");
        Require(arguments.Contains("value with spaces"), "The process lost an argument boundary.");
    }
}

internal sealed class ProbeAdapter : JsonGameAdapterBase
{
    private readonly string engine;
    public bool Ready { get; set; } = true;
    public bool CancelSetup { get; set; }
    public int ApplyCalls { get; private set; }
    public int SetupStarted { get; private set; }
    public int SetupCancelled { get; private set; }
    public int SetupSucceeded { get; private set; }
    public List<bool> PlanCalls { get; } = [];
    public string AuthoritativeMarker => Path.Combine(DataRoot, "authoritative-marker.txt");
    public string NormalReceipt => Path.Combine(DataRoot, "normal-receipt.json");
    public string ResumeReceipt => Path.Combine(DataRoot, "resume-receipt.json");

    public ProbeAdapter(string root, string engine) : base(
        new GameDefinition("ui-probe", "Synthetic UI Probe", "A private integration probe.", new[] {
            new OptionDefinition("display.integer", "Display", "Integer scaling", OptionKind.Boolean, OptionValue.From(true), "Synthetic boolean setting.") },
            HasSetup: true, CanResume: true),
        new[] { new ProfileDescriptor("original", "Synthetic original", "original") }, root)
    {
        this.engine = engine;
        if (!File.Exists(engine)) throw new FileNotFoundException("Build the synthetic SampleEngine fixture first.", engine);
        File.WriteAllText(AuthoritativeMarker, "authoritative original data");
    }

    public string SettingsPath(ProfileDescriptor profile) => Path.Combine(ProfileDirectory(profile), "settings.json");

    public override void ApplySettings(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, string expectedRevision)
    {
        base.ApplySettings(profile, values, expectedRevision);
        ApplyCalls++;
    }

    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) =>
        new(Ready, Ready ? "Synthetic game data ready." : "Synthetic game data needs setup.");

    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume)
    {
        PlanCalls.Add(resume);
        return new(engine, Path.GetDirectoryName(engine)!, new[] { "--receipt", resume ? ResumeReceipt : NormalReceipt, resume ? "resume" : "normal", "value with spaces" });
    }

    public override async Task SetupAsync(ProfileDescriptor profile, IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        SetupStarted++;
        progress?.Report(new("Preparing synthetic game data…", .2));
        try
        {
            await Task.Delay(CancelSetup ? Timeout.Infinite : 60, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var settings = ReadSettings(profile);
            settings.Values["display.integer"] = OptionValue.From(true);
            base.ApplySettings(profile, settings.Values, settings.Revision);
            File.WriteAllText(AuthoritativeMarker, "synthetic setup completed");
            Ready = true;
            SetupSucceeded++;
            progress?.Report(new("Synthetic game data ready.", 1));
        }
        catch (OperationCanceledException) { SetupCancelled++; throw; }
    }
}
