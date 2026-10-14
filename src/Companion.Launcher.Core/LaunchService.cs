namespace Companion.Launcher.Core;

/// <summary>Host-provided game lifetime. Android hosts use activities, desktop hosts use processes.</summary>
public interface IGameLaunchService
{
    Task RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default);
}

public sealed class DesktopLaunchService : IGameLaunchService
{
    public async Task RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var game = ProcessGameRuntime.Start(plan);
        // Cancelling observation/closing a launcher must not terminate gameplay.
        await game.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        if (game.ExitCode != 0) throw new IOException($"The game exited with code {game.ExitCode}.");
    }
}

public static class CompanionDataPaths
{
    public static string DesktopRoot()
    {
        if (OperatingSystem.IsMacOS()) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "CompanionGames");
        if (OperatingSystem.IsLinux())
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (!string.IsNullOrEmpty(xdg) && Path.IsPathFullyQualified(xdg)) return Path.Combine(xdg, "CompanionGames");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "CompanionGames");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CompanionGames");
    }
}
