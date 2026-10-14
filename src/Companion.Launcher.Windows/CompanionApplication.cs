using System.Text.Json;
using Companion.Launcher.Core;

namespace Companion.Launcher.Windows;

/// <summary>Standard Windows entry point for a game adapter.</summary>
public static class CompanionApplication
{
    /// <summary>Runs the launcher, or renders every declared page with --smoke-test [fresh directory].</summary>
    [STAThread]
    public static int Run(IGameAdapter adapter, string[]? args = null)
    {
        args ??= Environment.GetCommandLineArgs().Skip(1).ToArray();
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var controller = new LauncherController(adapter);
            using var form = new LauncherForm(controller);
            var smoke = Array.IndexOf(args, "--smoke-test");
            if (smoke >= 0)
            {
                var destination = smoke + 1 < args.Length && !args[smoke + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[smoke + 1]
                    : Path.Combine(Path.GetTempPath(), "CompanionLauncher-smoke-" + Guid.NewGuid().ToString("N"));
                var images = form.RenderSmokeTest(destination);
                Console.WriteLine(JsonSerializer.Serialize(new { passed = true, output = Path.GetFullPath(destination), pages = images.Count }));
            }
            else Application.Run(form);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            if (!args.Contains("--smoke-test")) MessageBox.Show(error.Message, "Companion could not start", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
