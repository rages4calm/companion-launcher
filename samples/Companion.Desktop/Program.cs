using Avalonia;
using Companion.Launcher.Avalonia;
using Companion.Launcher.Core;
using Companion.Template;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        CompanionApp.AdapterFactory = () => new PortableDemoAdapter(Environment.GetEnvironmentVariable("COMPANION_DEMO_DATA") ?? Path.Combine(CompanionDataPaths.DesktopRoot(), "template-demo"));
        AppBuilder.Configure<CompanionApp>().UsePlatformDetect().WithInterFont().StartWithClassicDesktopLifetime(args);
    }
}
