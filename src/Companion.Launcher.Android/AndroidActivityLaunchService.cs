using global::Android.App;
using global::Android.Content;
using Companion.Launcher.Core;

namespace Companion.Launcher.Android;

/// <summary>Launches only the in-package activity explicitly chosen by the game host.</summary>
public sealed class AndroidActivityLaunchService : IGameLaunchService
{
    readonly Activity owner;
    readonly Type gameActivity;
    readonly string route;
    TaskCompletionSource? completion;
    public const int RequestCode = 17493;

    public AndroidActivityLaunchService(Activity owner, Type gameActivity, string route)
    {
        if (!typeof(Activity).IsAssignableFrom(gameActivity) || gameActivity == owner.GetType()) throw new ArgumentException("Choose a separate game Activity type.");
        this.owner = owner; this.gameActivity = gameActivity; this.route = route;
    }

    public Task RunAsync(LaunchPlan plan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (completion is not null) throw new InvalidOperationException("A game is already running.");
        if (plan.Executable != route || !Path.IsPathFullyQualified(plan.WorkingDirectory)) throw new InvalidOperationException("The launch plan does not match this embedded game route/session.");
        if (plan.Environment is { Count: > 0 }) throw new NotSupportedException("Android activities cannot receive process environment overrides. Translate settings in the game adapter.");
        Directory.CreateDirectory(plan.WorkingDirectory);
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = pending;
        try
        {
            var intent = new Intent(owner, gameActivity);
            intent.PutExtra("companion.arguments", plan.Arguments.ToArray());
            intent.PutExtra("companion.sessionDirectory", plan.WorkingDirectory);
#pragma warning disable CA1422 // Activity-result bridge deliberately works with embedded legacy/SDL game activities.
            owner.StartActivityForResult(intent, RequestCode);
#pragma warning restore CA1422
        }
        catch { completion = null; throw; }
        // Once started, cancellation does not finish the game. Its own activity controls its lifetime.
        return pending.Task;
    }

    /// <summary>Forward the launcher's OnActivityResult here. Unknown result codes are not consumed.</summary>
    public bool OnActivityResult(int requestCode, Result resultCode)
    {
        if (requestCode != RequestCode) return false;
        var pending = completion; completion = null;
        if (resultCode == Result.Ok || resultCode == Result.Canceled) pending?.TrySetResult();
        else pending?.TrySetException(new IOException("The embedded game activity reported a failure."));
        return true;
    }
}
