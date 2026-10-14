using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Companion.Launcher.Core;

namespace Companion.Launcher.Avalonia;

public sealed class CompanionApp : Application
{
    public static Func<IGameAdapter>? AdapterFactory { get; set; }
    public static Func<IGameLaunchService>? LaunchServiceFactory { get; set; }
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        CompanionView CreateView() => new(new LauncherController((AdapterFactory ?? throw new InvalidOperationException("Set CompanionApp.AdapterFactory before starting the host."))()), LaunchServiceFactory?.Invoke() ?? new DesktopLaunchService());
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var view = CreateView();
            var window = new Window { Title = view.Controller.Definition.Title + " Companion", Width = 1120, Height = 800, MinWidth = 360, MinHeight = 440, Content = view };
            window.Closing += (_, args) => { if (!view.CanClose) { args.Cancel = true; view.RequestClose(); } };
            desktop.MainWindow = window;
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime android) android.MainViewFactory = CreateView;
        else if (ApplicationLifetime is ISingleViewApplicationLifetime mobile) mobile.MainView = CreateView();
        base.OnFrameworkInitializationCompleted();
    }
}
