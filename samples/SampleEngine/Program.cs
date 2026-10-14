using System.Text.Json;

// A synthetic process fixture, not a game. It exercises argument boundaries, profile isolation and cancellation.
if (args.Length > 0 && args[0] == "--wait")
{
    if (args.Length > 1) File.WriteAllText(args[1], Environment.ProcessId.ToString());
    await Task.Delay(TimeSpan.FromSeconds(60));
    return 0;
}
var receipt = Array.IndexOf(args, "--receipt");
if (receipt < 0 || receipt + 1 >= args.Length) return 2;
var output = Path.GetFullPath(args[receipt + 1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new { arguments = args, workingDirectory = Environment.CurrentDirectory, marker = Environment.GetEnvironmentVariable("COMPANION_TEST_MARKER") }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Synthetic engine run completed.");
Console.Error.WriteLine("Synthetic diagnostic channel.");
return args.Contains("--fail") ? 7 : 0;
