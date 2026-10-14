using System.Text.Json;

namespace Companion.Launcher.Core;

public enum OptionKind { Boolean, Integer, Choice, Text }

public sealed record ChoiceItem(string Value, string Label);

public sealed record LauncherTheme(string Background, string Surface, string Accent, string Text, string Muted)
{
    public static LauncherTheme Default { get; } = new("#10121A", "#1B1E2B", "#AD8DFF", "#F1F2F7", "#A3A8BB");
}

public sealed record OptionDefinition(
    string Id, string Section, string Label, OptionKind Kind, JsonElement DefaultValue,
    string Help = "", int? Minimum = null, int? Maximum = null, IReadOnlyList<ChoiceItem>? Choices = null);

public sealed record GameDefinition(
    string Id, string Title, string Description, IReadOnlyList<OptionDefinition> Options,
    bool HasSetup = false, bool HasAchievements = false, bool CanResume = false, LauncherTheme? Branding = null)
{
    public LauncherTheme Theme => Branding ?? LauncherTheme.Default;
}

public sealed record ProfileDescriptor(string Id, string Title, string EditionId, string? SeedId = null);
public sealed record SettingsSnapshot(Dictionary<string, JsonElement> Values, string Revision);
public sealed record Readiness(bool Ready, string Summary);
public sealed record OperationProgress(string Detail, double? Fraction = null);

public sealed record LaunchPlan(
    string Executable, string WorkingDirectory, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record AchievementEntry(string Id, string Title, string Description, uint Target, uint Progress, ulong UnlockedAt);
public sealed record AchievementSnapshot(string Title, IReadOnlyList<AchievementEntry> Entries);

public interface IGameAdapter
{
    GameDefinition Definition { get; }
    IReadOnlyList<ProfileDescriptor> Profiles { get; }
    SettingsSnapshot ReadSettings(ProfileDescriptor profile);
    IReadOnlyList<string> ValidateCustom(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values);
    void ApplySettings(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, string expectedRevision);
    Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values);
    Task SetupAsync(ProfileDescriptor profile, IProgress<OperationProgress>? progress, CancellationToken cancellationToken);
    LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume);
    AchievementSnapshot ReadAchievements(ProfileDescriptor profile);
}

public sealed class SettingsValidationException : InvalidOperationException
{
    public IReadOnlyList<string> Errors { get; }
    public SettingsValidationException(IReadOnlyList<string> errors) : base(string.Join(System.Environment.NewLine, errors)) => Errors = errors;
}

public sealed class SettingsConflictException : IOException
{
    public SettingsConflictException() : base("Settings changed outside this window. Revert to reload them before applying your changes.") { }
}

public static class OptionValue
{
    public static JsonElement From<T>(T value) => JsonSerializer.SerializeToElement(value);
}
