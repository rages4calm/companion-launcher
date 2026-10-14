using System.Security.Cryptography;
using System.Text.Json;

namespace Companion.Launcher.Core;

/// <summary>Authoritative settings for new integrations; existing native formats must supply their own adapter.</summary>
public sealed class JsonSettingsStore
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };
    public string FilePath { get; }

    public JsonSettingsStore(string path) => FilePath = Path.GetFullPath(path);

    public SettingsSnapshot Read()
    {
        if (!File.Exists(FilePath)) return new(new(StringComparer.Ordinal), "missing");
        var bytes = ReadBounded(FilePath);
        using var document = ParseJson(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Settings must contain a JSON object.");
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
            if (!values.TryAdd(property.Name, property.Value.Clone())) throw new InvalidDataException("Settings contain a duplicate key.");
        return new(values, Revision(bytes));
    }

    public void Write(IReadOnlyDictionary<string, JsonElement> values, string expectedRevision)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(values, Format);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Settings exceed the size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // The lock coordinates Companion writers; the revision check also catches external edits made before Apply.
        using var gate = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var actualRevision = File.Exists(FilePath) ? Revision(ReadBounded(FilePath)) : "missing";
        if (expectedRevision != actualRevision) throw new SettingsConflictException();
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static byte[] ReadBounded(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (file.Length > MaximumBytes) throw new InvalidDataException("JSON file exceeds the 1 MiB size limit.");
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        return bytes;
    }

    internal static JsonDocument ReadJsonDocument(string path) => ParseJson(ReadBounded(path));

    private static JsonDocument ParseJson(byte[] bytes)
    {
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
        return JsonDocument.Parse(bytes.AsMemory(offset));
    }

    private static string Revision(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
