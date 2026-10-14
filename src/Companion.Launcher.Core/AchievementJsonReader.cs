using System.Text.Json;
using System.Text.RegularExpressions;

namespace Companion.Launcher.Core;

/// <summary>Read-only browser for Companion Achievements schema v1. Awarding belongs to the game.</summary>
public static partial class AchievementJsonReader
{
    [GeneratedRegex("^[a-z0-9._-]{1,80}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    public static AchievementSnapshot Read(string catalogPath, string? progressPath, string gameId, string editionId, string? seedId = null)
    {
        ValidateNamespace(gameId);
        ValidateNamespace(editionId);
        if (seedId is not null) ValidateNamespace(seedId);
        using var catalog = JsonSettingsStore.ReadJsonDocument(catalogPath);
        var root = catalog.RootElement;
        Fields(root, new[] { "schemaVersion", "gameId", "editionId", "catalogVersion", "title", "achievements" });
        Version(root);
        Namespace(root, gameId, editionId);
        var title = Text(root.GetProperty("title"), 200);
        var array = root.GetProperty("achievements");
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is < 1 or > 64) throw new InvalidDataException("Catalog must contain 1-64 achievements.");
        var definitions = new Dictionary<string, AchievementEntry>(StringComparer.Ordinal);
        foreach (var entry in array.EnumerateArray())
        {
            Fields(entry, new[] { "id", "title", "description", "event", "rule", "target" }, new[] { "icon" });
            var id = Id(entry.GetProperty("id"));
            _ = Id(entry.GetProperty("event"));
            if (entry.GetProperty("rule").GetString() is not ("counter" or "milestone")) throw new InvalidDataException("Unsupported achievement rule.");
            var target = Number(entry.GetProperty("target"), uint.MaxValue);
            if (target == 0) throw new InvalidDataException("Achievement target must be positive.");
            if (entry.TryGetProperty("icon", out var iconValue))
            {
                var icon = Text(iconValue, 240);
                if (icon.StartsWith('/') || icon.Contains('\\') || icon.Contains(':') || icon.Split('/').Any(part => part is "" or "." or ".."))
                    throw new InvalidDataException("Achievement icon must be a safe relative path.");
            }
            if (!definitions.TryAdd(id, new(id, Text(entry.GetProperty("title"), 200), Text(entry.GetProperty("description"), 2000), (uint)target, 0, 0)))
                throw new InvalidDataException("Duplicate achievement ID.");
        }

        if (progressPath is not null && File.Exists(progressPath))
        {
            using var progress = JsonSettingsStore.ReadJsonDocument(progressPath);
            var state = progress.RootElement;
            Fields(state, new[] { "schemaVersion", "gameId", "editionId", "seedId", "catalogVersion", "achievements" });
            Version(state);
            Namespace(state, gameId, editionId);
            var seedValue = state.GetProperty("seedId");
            var actualSeed = seedValue.ValueKind == JsonValueKind.Null ? null : NamespaceId(seedValue);
            if (seedId is not null) ValidateNamespace(seedId);
            if (actualSeed != seedId) throw new InvalidDataException("Achievement progress belongs to a different seed.");
            var states = state.GetProperty("achievements");
            if (states.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Achievement progress must contain an array.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in states.EnumerateArray())
            {
                Fields(entry, new[] { "id", "progress", "unlockedAt" });
                var id = Id(entry.GetProperty("id"));
                if (!seen.Add(id)) throw new InvalidDataException("Duplicate achievement progress ID.");
                if (!definitions.TryGetValue(id, out var definition)) continue;
                var amount = Number(entry.GetProperty("progress"), definition.Target);
                var time = Number(entry.GetProperty("unlockedAt"), long.MaxValue);
                if ((amount == definition.Target) != (time > 0)) throw new InvalidDataException("Achievement progress and unlock time disagree.");
                definitions[id] = definition with { Progress = (uint)amount, UnlockedAt = time };
            }
        }
        return new(title, definitions.Values.ToArray());
    }

    private static void Version(JsonElement root)
    {
        if (Number(root.GetProperty("schemaVersion"), uint.MaxValue) != 1 || Number(root.GetProperty("catalogVersion"), uint.MaxValue) == 0)
            throw new InvalidDataException("Unsupported achievement schema or catalog version.");
    }

    private static void Namespace(JsonElement root, string gameId, string editionId)
    {
        if (NamespaceId(root.GetProperty("gameId")) != gameId || NamespaceId(root.GetProperty("editionId")) != editionId)
            throw new InvalidDataException("Achievements belong to a different game or edition.");
    }

    private static string NamespaceId(JsonElement value)
    {
        var id = Id(value);
        ValidateNamespace(id);
        return id;
    }

    private static void ValidateNamespace(string id)
    {
        var stem = id.Split('.')[0].ToUpperInvariant();
        if (!IdPattern().IsMatch(id) || id is "." or ".." || id.EndsWith('.') || stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9'))
            throw new InvalidDataException("Unsafe achievement namespace.");
    }

    private static string Id(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || !IdPattern().IsMatch(value.GetString()!)) throw new InvalidDataException("Invalid achievement identifier.");
        return value.GetString()!;
    }

    private static string Text(JsonElement value, int maximum)
    {
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } text || text.Length is < 1 || text.Contains('\0'))
            throw new InvalidDataException("Invalid achievement text.");
        for (var index = 0; index < text.Length; index++)
        {
            if (char.IsHighSurrogate(text[index]))
            {
                if (++index >= text.Length || !char.IsLowSurrogate(text[index])) throw new InvalidDataException("Invalid Unicode in achievement text.");
            }
            else if (char.IsLowSurrogate(text[index])) throw new InvalidDataException("Invalid Unicode in achievement text.");
        }
        if (text.EnumerateRunes().Count() > maximum) throw new InvalidDataException("Achievement text exceeds its size limit.");
        return text;
    }

    private static ulong Number(JsonElement value, ulong maximum)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out var number) || number > maximum) throw new InvalidDataException("Invalid achievement number.");
        return number;
    }

    private static void Fields(JsonElement value, IReadOnlyCollection<string> required, IReadOnlyCollection<string>? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Achievement data must contain objects.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name) || (!required.Contains(property.Name) && optional?.Contains(property.Name) != true))
                throw new InvalidDataException("Duplicate or unsupported achievement field.");
        }
        if (required.Any(field => !seen.Contains(field))) throw new InvalidDataException("Missing achievement field.");
    }
}
