using System.Text.Json;

namespace Companion.Launcher.Core;

public abstract class JsonGameAdapterBase : IGameAdapter
{
    public GameDefinition Definition { get; }
    public IReadOnlyList<ProfileDescriptor> Profiles { get; }
    public string DataRoot { get; }

    protected JsonGameAdapterBase(GameDefinition definition, IReadOnlyList<ProfileDescriptor> profiles, string dataRoot)
    {
        OptionValidation.ValidateDefinition(definition, profiles);
        Definition = definition;
        Profiles = profiles;
        DataRoot = Path.GetFullPath(dataRoot);
    }

    public string ProfileDirectory(ProfileDescriptor profile)
    {
        if (!Profiles.Contains(profile)) throw new ArgumentException("Profile is not declared by this adapter.");
        return Identity.ProfileDirectory(DataRoot, Definition.Id, profile);
    }

    protected JsonSettingsStore Store(ProfileDescriptor profile) => new(Path.Combine(ProfileDirectory(profile), "settings.json"));
    public virtual SettingsSnapshot ReadSettings(ProfileDescriptor profile) => Store(profile).Read();
    public virtual IReadOnlyList<string> ValidateCustom(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values) => Array.Empty<string>();
    public virtual void ApplySettings(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, string expectedRevision)
    {
        var errors = OptionValidation.Validate(Definition, values).Concat(ValidateCustom(profile, values)).ToArray();
        if (errors.Length > 0) throw new SettingsValidationException(errors);
        Store(profile).Write(values, expectedRevision);
    }
    public abstract Readiness CheckReady(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values);
    public abstract LaunchPlan BuildLaunchPlan(ProfileDescriptor profile, IReadOnlyDictionary<string, JsonElement> values, bool resume);
    public virtual Task SetupAsync(ProfileDescriptor profile, IProgress<OperationProgress>? progress, CancellationToken cancellationToken) =>
        Task.FromException(new NotSupportedException("This adapter does not provide setup."));
    public virtual AchievementSnapshot ReadAchievements(ProfileDescriptor profile) => new("Achievements", Array.Empty<AchievementEntry>());
}
