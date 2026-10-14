using System.Text.Json;
using System.Text.RegularExpressions;

namespace Companion.Launcher.Core;

public static partial class Identity
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,79}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static string Validate(string id, string field = "identifier")
    {
        if (string.IsNullOrEmpty(id) || !IdPattern().IsMatch(id) || id.EndsWith('.') ||
            IsReserved(id.Split('.')[0]))
            throw new ArgumentException($"Invalid {field}: use 1-80 lowercase letters, digits, dots, underscores or hyphens, starting with a letter or digit; avoid device names and trailing dots.");
        return id;
    }

    private static bool IsReserved(string name) => name is "con" or "prn" or "aux" or "nul" ||
        (name.Length == 4 && (name.StartsWith("com", StringComparison.Ordinal) || name.StartsWith("lpt", StringComparison.Ordinal)) && name[3] is >= '1' and <= '9');

    public static string ProfileDirectory(string dataRoot, string gameId, ProfileDescriptor profile)
    {
        Validate(gameId, "game ID");
        Validate(profile.Id, "profile ID");
        Validate(profile.EditionId, "edition ID");
        var root = Path.GetFullPath(dataRoot);
        var path = Path.Combine(root, gameId, profile.EditionId, profile.Id);
        return profile.SeedId is null ? Path.Combine(path, "unseeded") : Path.Combine(path, "seeded", Validate(profile.SeedId, "seed ID"));
    }
}

public static class OptionValidation
{
    public static IReadOnlyList<string> Validate(GameDefinition definition, IReadOnlyDictionary<string, JsonElement> values)
    {
        var errors = new List<string>();
        foreach (var option in definition.Options)
        {
            if (!values.TryGetValue(option.Id, out var value)) errors.Add($"{option.Label}: a value is required.");
            else if (ValidateValue(option, value) is { } error) errors.Add(error);
        }
        return errors;
    }

    public static string? ValidateValue(OptionDefinition option, JsonElement value)
    {
        var valid = option.Kind switch
        {
            OptionKind.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            OptionKind.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) &&
                                  (!option.Minimum.HasValue || number >= option.Minimum) && (!option.Maximum.HasValue || number <= option.Maximum),
            OptionKind.Choice => value.ValueKind == JsonValueKind.String && option.Choices is { Count: > 0 } &&
                                 option.Choices.Any(choice => choice.Value == value.GetString()),
            OptionKind.Text => value.ValueKind == JsonValueKind.String && (value.GetString()?.Length ?? 0) <= 16384,
            _ => false
        };
        return valid ? null : $"{option.Label}: invalid {option.Kind.ToString().ToLowerInvariant()} value" +
            (option.Kind == OptionKind.Integer ? $" (range {option.Minimum?.ToString() ?? "any"} to {option.Maximum?.ToString() ?? "any"})." : ".");
    }

    public static void ValidateDefinition(GameDefinition definition, IReadOnlyList<ProfileDescriptor> profiles)
    {
        Identity.Validate(definition.Id, "game ID");
        if (string.IsNullOrWhiteSpace(definition.Title) || profiles.Count == 0) throw new ArgumentException("Game title and at least one profile are required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in definition.Options)
        {
            Identity.Validate(option.Id, "option ID");
            if (!ids.Add(option.Id)) throw new ArgumentException($"Duplicate option ID: {option.Id}");
            if (string.IsNullOrWhiteSpace(option.Label) || string.IsNullOrWhiteSpace(option.Section) || option.Minimum > option.Maximum)
                throw new ArgumentException($"Invalid option definition: {option.Id}");
            if (option.Kind == OptionKind.Choice && (option.Choices is not { Count: > 0 } || option.Choices.Select(c => c.Value).Distinct(StringComparer.Ordinal).Count() != option.Choices.Count))
                throw new ArgumentException($"Choices must be nonempty and unique: {option.Id}");
            if (ValidateValue(option, option.DefaultValue) is { } error) throw new ArgumentException($"Invalid default. {error}");
        }
        ids.Clear();
        foreach (var profile in profiles)
        {
            Identity.Validate(profile.Id, "profile ID");
            Identity.Validate(profile.EditionId, "edition ID");
            if (profile.SeedId is { } seed) Identity.Validate(seed, "seed ID");
            if (!ids.Add(profile.Id)) throw new ArgumentException($"Duplicate profile ID: {profile.Id}");
        }
    }
}
