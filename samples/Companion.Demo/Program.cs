using Companion.Launcher.Windows;

namespace Companion.Demo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => CompanionApplication.Run(new DemoAdapter(), args);
}
