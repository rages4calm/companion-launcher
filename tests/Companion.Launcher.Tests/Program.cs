using System.Diagnostics;
using System.Text.Json;
using Companion.Launcher.Core;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var temporary = Path.Combine(root, "build", "test-data", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; }
void Throws<T>(Action action, string label) where T : Exception
{
    try { action(); } catch (T) { checks++; return; }
    throw new Exception("FAIL: " + label + " did not throw " + typeof(T).Name);
}

var adapter = new TestAdapter(temporary);
var controller = new LauncherController(adapter);
Check(!controller.HasChanges && controller.Snapshot.Values["volume"].GetInt32() == 80, "declared defaults and clean draft");
controller.SetOption("volume", OptionValue.From(45));
Throws<InvalidOperationException>(() => controller.BuildLaunchPlan(), "draft cannot launch");
Throws<InvalidOperationException>(() => controller.SelectProfile("other"), "draft cannot switch profile");
Check(controller.Profile.Id == "original" && controller.Snapshot.Values["volume"].GetInt32() == 45, "rejected profile switch preserves draft");
Throws<SettingsValidationException>(() => controller.SetOption("volume", OptionValue.From(101)), "range rejected");
Throws<SettingsValidationException>(() => controller.SetOption("integer", OptionValue.From("true")), "bool string rejected");
Throws<ArgumentException>(() => controller.SetOption("unknown", OptionValue.From(1)), "undeclared options cannot be edited");
controller.Apply();
Check(!controller.HasChanges && File.Exists(Path.Combine(adapter.ProfileDirectory(controller.Profile), "settings.json")), "apply writes authoritative profile settings");
var store = new JsonSettingsStore(Path.Combine(adapter.ProfileDirectory(controller.Profile), "settings.json"));
var stored = store.Read();
stored.Values["engine.unknown"] = OptionValue.From(new { nested = 7, keep = true });
store.Write(stored.Values, stored.Revision);
controller.Reload();
controller.SetOption("volume", OptionValue.From(33));
controller.Apply();
Check(store.Read().Values["engine.unknown"].GetProperty("nested").GetInt32() == 7, "unknown native-owned fields preserved");
var second = new LauncherController(adapter);
controller.SetOption("volume", OptionValue.From(12));
second.SetOption("volume", OptionValue.From(22));
second.Apply();
var beforeConflict = File.ReadAllBytes(store.FilePath);
Throws<SettingsConflictException>(() => controller.Apply(), "stale draft cannot overwrite external changes");
Check(File.ReadAllBytes(store.FilePath).SequenceEqual(beforeConflict) && controller.HasChanges && controller.Snapshot.Values["volume"].GetInt32() == 12,
    "conflict leaves file and unsaved draft intact");
controller.Reload();
Check(!controller.HasChanges && controller.Snapshot.Values["volume"].GetInt32() == 22, "revert reloads winning writer");
controller.SelectProfile("other");
Check(controller.Snapshot.Values["volume"].GetInt32() == 80, "edition settings isolated");
var p = new ProfileDescriptor("p", "Plain", "original");
Check(Identity.ProfileDirectory(temporary, "game-a", p) != Identity.ProfileDirectory(temporary, "game-b", p), "games isolated");
Check(Identity.ProfileDirectory(temporary, "game-a", p) != Identity.ProfileDirectory(temporary, "game-a", p with { SeedId = "unseeded" }), "null seed differs from literal unseeded");
Check(Identity.ProfileDirectory(temporary, "game-a", p with { SeedId = "a" }) != Identity.ProfileDirectory(temporary, "game-a", p with { SeedId = "b" }), "seeds isolated");
foreach (var id in new[] { "..", "../outside", "con", "con.txt", "lpt1", "bad.", "Game", "bad id", "demo\n" }) Throws<ArgumentException>(() => Identity.Validate(id), "unsafe ID " + id);
var corrupt = Path.Combine(temporary, "corrupt.json");
File.WriteAllText(corrupt, "{\"volume\":1,\"volume\":2}");
Throws<InvalidDataException>(() => new JsonSettingsStore(corrupt).Read(), "duplicate settings keys rejected");
var corruptBytes = File.ReadAllBytes(corrupt);
Throws<InvalidDataException>(() => new LauncherController(new CorruptAdapter(temporary, corrupt)), "invalid settings not silently reset");
Check(File.ReadAllBytes(corrupt).SequenceEqual(corruptBytes), "invalid document untouched");
controller.SelectProfile("original");
var beforeSetup = File.ReadAllBytes(store.FilePath);
try { await controller.SetupAsync(null, CancellationToken.None); throw new Exception("FAIL setup should fail"); }
catch (IOException) { checks++; }
Check(File.ReadAllBytes(store.FilePath).SequenceEqual(beforeSetup), "failed adapter setup leaves authoritative settings unchanged");
Throws<NotSupportedException>(() => controller.BuildLaunchPlan(resume: true), "undeclared resume rejected");

var catalog = Path.Combine(root, "samples", "Companion.Demo", "Data", "catalog.json");
var progress = Path.Combine(root, "samples", "Companion.Demo", "Data", "progress.json");
var browser = AchievementJsonReader.Read(catalog, progress, "example-trail", "original");
Check(browser.Entries.Count == 3 && browser.Entries.Count(entry => entry.UnlockedAt > 0) == 1 && browser.Entries[1].Progress == 2, "shared achievement schema earned/partial/locked");
Throws<InvalidDataException>(() => AchievementJsonReader.Read(catalog, progress, "different", "original"), "cross-game catalog rejected");
Throws<InvalidDataException>(() => AchievementJsonReader.Read(catalog, progress, "example-trail", "redux"), "cross-edition catalog rejected");
Throws<InvalidDataException>(() => AchievementJsonReader.Read(catalog, progress, "example-trail", "original", "seed"), "cross-seed progress rejected");
Check(AchievementJsonReader.Read(catalog, null, "example-trail", "original").Entries.All(entry => entry.UnlockedAt == 0), "missing progress starts locked without awards");
var mutatedProgress = Path.Combine(temporary, "achievement-progress.json");
var bomCatalog = Path.Combine(temporary, "bom-catalog.json");
var unicodeCatalog = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(catalog))!;
unicodeCatalog["title"] = string.Concat(Enumerable.Repeat("🎮", 200));
File.WriteAllText(bomCatalog, unicodeCatalog.ToJsonString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
Check(AchievementJsonReader.Read(bomCatalog, progress, "example-trail", "original").Title.EnumerateRunes().Count() == 200, "BOM and Unicode scalar limits match achievement tooling");
unicodeCatalog["achievements"]![0]!["id"] = "trail.first-win\n";
File.WriteAllText(bomCatalog, unicodeCatalog.ToJsonString());
Throws<InvalidDataException>(() => AchievementJsonReader.Read(bomCatalog, progress, "example-trail", "original"), "achievement IDs reject terminal newline");
var obj = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(progress))!;
obj["achievements"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("{\"id\":\"future.id\",\"progress\":\"opaque\",\"unlockedAt\":null}"));
File.WriteAllText(mutatedProgress, obj.ToJsonString());
Check(AchievementJsonReader.Read(catalog, mutatedProgress, "example-trail", "original").Entries.Count == 3, "future achievement ID ignored read-only");
var unchanged = File.ReadAllBytes(mutatedProgress);
_ = AchievementJsonReader.Read(catalog, mutatedProgress, "example-trail", "original");
Check(File.ReadAllBytes(mutatedProgress).SequenceEqual(unchanged), "browser never rewrites progress");
obj["achievements"]![1]!["unlockedAt"] = 1;
File.WriteAllText(mutatedProgress, obj.ToJsonString());
Throws<InvalidDataException>(() => AchievementJsonReader.Read(catalog, mutatedProgress, "example-trail", "original"), "inconsistent unlock state rejected");

var sdkIndex = Array.IndexOf(args, "--achievement-sdk");
if (sdkIndex >= 0)
{
    var sdk = args[sdkIndex + 1];
    var sdkCatalog = Path.Combine(sdk, "examples", "trail", "catalog.json");
    var runtime = Path.Combine(sdk, "build", "demo-data", "example-trail", "original", "unseeded", "progress.json");
    var native = AchievementJsonReader.Read(sdkCatalog, runtime, "example-trail", "original");
    Check(File.Exists(runtime) && native.Entries.Count(entry => entry.UnlockedAt > 0) == 2 && native.Entries[1].Progress == 2,
        "native C host committed progress consumed by managed launcher");
}

var exe = Path.Combine(AppContext.BaseDirectory, "Engine", OperatingSystem.IsWindows() ? "SampleEngine.exe" : "SampleEngine");
var receipt = Path.Combine(temporary, "arguments with spaces.json");
var tricky = new[] { "with space", "quote\"inside", "ends\\", "$() & ;", "", "日本語" };
var allArguments = new[] { "--receipt", receipt }.Concat(tricky).ToArray();
var plan = new LaunchPlan(exe, temporary, allArguments, new Dictionary<string, string> { ["COMPANION_TEST_MARKER"] = "test-value" });
var result = await SetupProcessRunner.RunAsync(plan);
Check(result.ExitCode == 0 && result.StandardOutput.Contains("completed") && result.StandardError.Contains("diagnostic"), "drains both tool output channels");
using (var output = JsonDocument.Parse(File.ReadAllText(receipt)))
{
    Check(output.RootElement.GetProperty("arguments").EnumerateArray().Select(v => v.GetString()).SequenceEqual(allArguments), "structured arguments survive spaces, quotes, metacharacters, empty strings and Unicode");
    Check(output.RootElement.GetProperty("workingDirectory").GetString() == temporary && output.RootElement.GetProperty("marker").GetString() == "test-value", "explicit working directory and environment preserved");
}
var failed = await SetupProcessRunner.RunAsync(plan with { Arguments = allArguments.Append("--fail").ToArray() });
Check(failed.ExitCode == 7, "tool failure exit status preserved");
Throws<FileNotFoundException>(() => ProcessGameRuntime.Start(plan with { Executable = "relative.exe" }), "relative executable cannot launch");
Throws<DirectoryNotFoundException>(() => ProcessGameRuntime.Start(plan with { WorkingDirectory = Path.Combine(temporary, "missing") }), "missing working directory cannot launch");
var pidFile = Path.Combine(temporary, "child.pid");
using (var cancellation = new CancellationTokenSource())
{
    var pending = SetupProcessRunner.RunAsync(plan with { Arguments = new[] { "--wait", pidFile } }, cancellation.Token);
    for (var n = 0; n < 100 && !File.Exists(pidFile); n++) await Task.Delay(20);
    Check(File.Exists(pidFile), "cancellation fixture actually started");
    var pid = int.Parse(File.ReadAllText(pidFile));
    cancellation.Cancel();
    try { await pending; throw new Exception("FAIL cancelled child should throw"); } catch (OperationCanceledException) { checks++; }
    var alive = false;
    try { using var child = Process.GetProcessById(pid); alive = !child.HasExited; } catch (ArgumentException) { }
    Check(!alive, "cancelled owned setup child stopped");
}
var independent = ProcessGameRuntime.Start(plan with { Arguments = new[] { "--wait" } });
var independentId = independent.ProcessId;
independent.Dispose();
using (var child = Process.GetProcessById(independentId))
{
    Check(!child.HasExited, "disposing launcher process handle does not kill game");
    child.Kill(entireProcessTree: true);
    await child.WaitForExitAsync();
}
Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks, data = temporary }));
return 0;

class TestAdapter : JsonGameAdapterBase
{
    public TestAdapter(string root) : base(new("test-game", "Test", "Synthetic test", new[] {
        new OptionDefinition("volume", "Audio", "Volume", OptionKind.Integer, OptionValue.From(80), Minimum: 0, Maximum: 100),
        new OptionDefinition("integer", "Display", "Integer scaling", OptionKind.Boolean, OptionValue.From(true)) }, HasSetup: true),
        new[] { new ProfileDescriptor("original", "Original", "original"), new ProfileDescriptor("other", "Other", "other") }, root) { }
    public override Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) => new(true, "Test ready");
    public override LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume) => new("test.exe", DataRoot, Array.Empty<string>());
    public override Task SetupAsync(ProfileDescriptor profile, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) => Task.FromException(new IOException("Synthetic setup failure"));
}

class CorruptAdapter(string root, string file) : TestAdapter(root)
{
    public override SettingsSnapshot ReadSettings(ProfileDescriptor profile) => new JsonSettingsStore(file).Read();
}
