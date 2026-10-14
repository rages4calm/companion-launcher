using System.Text.Json;

namespace Companion.Launcher.Core;

/// <summary>Stages edits; adapters remain responsible for translating them into the game's settings format.</summary>
public sealed class LauncherController
{
    private SettingsSnapshot saved;
    public IGameAdapter Adapter { get; }
    public GameDefinition Definition => Adapter.Definition;
    public IReadOnlyList<ProfileDescriptor> Profiles => Adapter.Profiles;
    public ProfileDescriptor Profile { get; private set; }
    public SettingsSnapshot Snapshot { get; private set; }
    public bool HasChanges => saved.Values.Count != Snapshot.Values.Count || saved.Values.Any(pair =>
        !Snapshot.Values.TryGetValue(pair.Key, out var value) || value.GetRawText() != pair.Value.GetRawText());

    public LauncherController(IGameAdapter adapter)
    {
        Adapter = adapter;
        OptionValidation.ValidateDefinition(adapter.Definition, adapter.Profiles);
        Profile = Profiles[0];
        saved = Snapshot = new(new(), "missing");
        Reload();
    }

    public void SelectProfile(string id)
    {
        if (id == Profile.Id) return;
        if (HasChanges) throw new InvalidOperationException("Apply or revert your changes before switching profiles.");
        var selected = Profiles.FirstOrDefault(profile => profile.Id == id) ?? throw new ArgumentException("Unknown profile.", nameof(id));
        // A failed profile read must leave the current profile and draft intact.
        var next = ReadWithDefaults(selected);
        Profile = selected;
        saved = Copy(next);
        Snapshot = Copy(next);
    }

    public void SetOption(string id, JsonElement value)
    {
        var option = Definition.Options.FirstOrDefault(option => option.Id == id) ?? throw new ArgumentException("Unknown option.", nameof(id));
        if (OptionValidation.ValidateValue(option, value) is { } error) throw new SettingsValidationException(new[] { error });
        Snapshot.Values[id] = value.Clone();
    }

    public void Apply()
    {
        var errors = OptionValidation.Validate(Definition, Snapshot.Values).Concat(Adapter.ValidateCustom(Profile, Snapshot.Values)).ToArray();
        if (errors.Length > 0) throw new SettingsValidationException(errors);
        Adapter.ApplySettings(Profile, Copy(Snapshot).Values, saved.Revision);
        Reload();
    }

    public void Reload()
    {
        var next = ReadWithDefaults(Profile);
        saved = Copy(next);
        Snapshot = Copy(next);
    }

    private SettingsSnapshot ReadWithDefaults(ProfileDescriptor profile)
    {
        var next = Copy(Adapter.ReadSettings(profile));
        foreach (var option in Definition.Options) next.Values.TryAdd(option.Id, option.DefaultValue.Clone());
        var errors = OptionValidation.Validate(Definition, next.Values);
        if (errors.Count > 0) throw new SettingsValidationException(errors);
        return next;
    }

    public Readiness GetReadiness() => Adapter.CheckReady(Profile, Snapshot.Values);

    public LaunchPlan BuildLaunchPlan(bool resume = false)
    {
        if (HasChanges) throw new InvalidOperationException("Apply or revert your changes before playing.");
        if (resume && !Definition.CanResume) throw new NotSupportedException("This game does not provide quick-save resume.");
        var readiness = GetReadiness();
        if (!readiness.Ready) throw new InvalidOperationException(readiness.Summary);
        return Adapter.BuildLaunchPlan(Profile, Copy(saved).Values, resume);
    }

    public Task SetupAsync(IProgress<OperationProgress>? progress, CancellationToken cancellationToken)
    {
        if (!Definition.HasSetup) throw new NotSupportedException("This game does not provide launcher setup.");
        if (HasChanges) throw new InvalidOperationException("Apply or revert your changes before setup.");
        return Adapter.SetupAsync(Profile, progress, cancellationToken);
    }

    public AchievementSnapshot ReadAchievements() => Definition.HasAchievements ? Adapter.ReadAchievements(Profile) : new("Achievements", Array.Empty<AchievementEntry>());

    private static SettingsSnapshot Copy(SettingsSnapshot value) => new(value.Values.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal), value.Revision);
}
