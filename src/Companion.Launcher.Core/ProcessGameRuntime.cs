using System.Diagnostics;

namespace Companion.Launcher.Core;

public static class ProcessGameRuntime
{
    public static RunningGame Start(LaunchPlan plan)
    {
        var process = new Process { StartInfo = CreateStartInfo(plan) };
        try
        {
            if (!process.Start()) throw new IOException("The game process could not be started.");
            return new(process);
        }
        catch { process.Dispose(); throw; }
    }

    internal static ProcessStartInfo CreateStartInfo(LaunchPlan plan)
    {
        if (!Path.IsPathFullyQualified(plan.Executable) || !File.Exists(plan.Executable)) throw new FileNotFoundException("Configure an existing absolute executable path.", plan.Executable);
        if (!Path.IsPathFullyQualified(plan.WorkingDirectory) || !Directory.Exists(plan.WorkingDirectory)) throw new DirectoryNotFoundException("The game's working directory does not exist.");
        var info = new ProcessStartInfo(plan.Executable) { WorkingDirectory = plan.WorkingDirectory, UseShellExecute = false };
        foreach (var argument in plan.Arguments) info.ArgumentList.Add(argument);
        if (plan.Environment is not null) foreach (var pair in plan.Environment) info.Environment[pair.Key] = pair.Value;
        return info;
    }
}

public sealed class RunningGame : IDisposable
{
    private readonly Process process;
    internal RunningGame(Process process) => this.process = process;
    public int ProcessId => process.Id;
    public bool HasExited => process.HasExited;
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => process.WaitForExitAsync(cancellationToken);
    public int ExitCode => process.ExitCode;
    /// <summary>Explicit caller action only. Closing the launcher does not call this.</summary>
    public void Stop() { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    public void Dispose() => process.Dispose();
}

public sealed record ToolResult(int ExitCode, string StandardOutput, string StandardError);

public static class SetupProcessRunner
{
    /// <summary>Run one adapter-owned setup tool. Cancellation stops only this child process tree.</summary>
    public static async Task<ToolResult> RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = ProcessGameRuntime.CreateStartInfo(plan);
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.CreateNoWindow = true;
        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            throw;
        }
        return new(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}
