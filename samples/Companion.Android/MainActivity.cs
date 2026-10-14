using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Companion.Launcher.Android;
using Companion.Launcher.Avalonia;
using Companion.Template;

namespace Companion.TemplateAndroid;

[Activity(Label = "Companion Template", MainLauncher = true, Exported = true,
    Theme = "@style/CompanionTheme", ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public sealed class MainActivity : AvaloniaMainActivity
{
    AndroidActivityLaunchService? launches;
    bool smokeStarted;
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        launches = new AndroidActivityLaunchService(this, typeof(DemoGameActivity), "embedded-demo");
        CompanionApp.AdapterFactory = () => new PortableDemoAdapter(Path.Combine(FilesDir!.AbsolutePath, "TemplateDemo"), mobile: true);
        CompanionApp.LaunchServiceFactory = () => launches;
        base.OnCreate(savedInstanceState);
    }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (launches?.OnActivityResult(requestCode, resultCode) != true) base.OnActivityResult(requestCode, resultCode, data);
    }
    protected override void OnResume()
    {
        base.OnResume();
        if (!smokeStarted && Intent?.GetBooleanExtra("companion.smoke", false) == true)
        {
            smokeStarted = true;
            new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!).PostDelayed(() => _ = RunSyntheticSmoke(), 800);
        }
    }
    async Task RunSyntheticSmoke()
    {
        try
        {
            var adapter = CompanionApp.AdapterFactory!();
            var controller = new Companion.Launcher.Core.LauncherController(adapter);
            controller.SetOption("player.label", Companion.Launcher.Core.OptionValue.From("Android round trip")); controller.Apply();
            await controller.SetupAsync(null, CancellationToken.None);
            var plan = controller.BuildLaunchPlan(true);
            await launches!.RunAsync(plan with { Arguments = plan.Arguments.Append("--auto-return").ToArray() });
            var restored = new Companion.Launcher.Core.LauncherController(adapter);
            if (restored.Snapshot.Values["player.label"].GetString() != "Android round trip" || !restored.GetReadiness().Ready) throw new IOException("Settings/readiness round trip failed.");
            global::Android.Util.Log.Info("CompanionTemplateSmoke", "PASS: native activity launched and returned; settings persisted; readiness restored.");
        }
        catch (Exception error) { global::Android.Util.Log.Error("CompanionTemplateSmoke", "FAIL: " + error); }
    }
}

[global::Android.App.Application]
public sealed class AndroidApp(nint reference, global::Android.Runtime.JniHandleOwnership ownership) : AvaloniaAndroidApplication<CompanionApp>(reference, ownership)
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder).WithInterFont();
}

[Activity(Label = "Synthetic game", Exported = false, Theme = "@style/CompanionTheme")]
public sealed class DemoGameActivity : Activity
{
    protected override void OnCreate(global::Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var stack = new global::Android.Widget.LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        stack.SetPadding(32, 64, 32, 32);
        var text = new global::Android.Widget.TextView(this) { Text = "Embedded game activity test\n\nNo game is included. The host passed these arguments:\n\n" + string.Join("\n", Intent?.GetStringArrayExtra("companion.arguments") ?? Array.Empty<string>()) };
        text.TextSize = 20; stack.AddView(text);
        var back = new global::Android.Widget.Button(this) { Text = "Return to Companion" };
        back.Click += (_, _) => { SetResult(Result.Ok); Finish(); }; stack.AddView(back); SetContentView(stack);
        if (Intent?.GetStringArrayExtra("companion.arguments")?.Contains("--auto-return") == true)
            new global::Android.OS.Handler(global::Android.OS.Looper.MainLooper!).PostDelayed(() => { SetResult(Result.Ok); Finish(); }, 500);
    }
}
