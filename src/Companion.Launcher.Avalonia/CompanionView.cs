using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Companion.Launcher.Core;

namespace Companion.Launcher.Avalonia;

/// <summary>Shared desktop/mobile view. Game behavior stays in the adapter and host launch service.</summary>
public sealed class CompanionView : UserControl
{
    readonly LauncherController controller;
    readonly IGameLaunchService launches;
    readonly StackPanel page = new() { Spacing = 12, Margin = new Thickness(20) };
    readonly StackPanel rail = new() { Spacing = 10, Margin = new Thickness(16) };
    readonly StackPanel footer = new() { Spacing = 8, Margin = new Thickness(16) };
    readonly TextBlock status = new() { Name = "status", TextWrapping = TextWrapping.Wrap };
    readonly ComboBox profiles = new() { Name = "profiles", MinWidth = 160, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly ComboBox navigation = new() { Name = "navigation", HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly Dictionary<string, string> pages = new();
    readonly Grid layout = new() { ColumnDefinitions = new ColumnDefinitions("220,*"), RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
    readonly IBrush background, surface, foreground, muted, accent;
    readonly List<Control> editControls = new();
    Button? apply, revert, play, resume;
    CancellationTokenSource? setup;
    bool changingProfile, busy;
    string current = "play";
    readonly Dictionary<string, string> editErrors = new();

    public LauncherController Controller => controller;
    public bool IsBusy => busy;
    public bool CanClose => !controller.HasChanges && editErrors.Count == 0 && setup is null;
    public string CurrentPage => current;
    public IReadOnlyList<string> PageIds => pages.Keys.ToArray();

    public CompanionView(LauncherController controller, IGameLaunchService launches)
    {
        this.controller = controller;
        this.launches = launches;
        var theme = controller.Definition.Theme;
        background = Brush(theme.Background, "#10121A"); surface = Brush(theme.Surface, "#1B1E2B");
        foreground = Brush(theme.Text, "#F1F2F7"); muted = Brush(theme.Muted, "#A3A8BB"); accent = Brush(theme.Accent, "#AD8DFF");
        Background = background;
        pages.Add("play", "Play");
        foreach (var section in controller.Definition.Options.Select(option => option.Section).Distinct()) pages.Add("section:" + section, section);
        if (controller.Definition.HasSetup) pages.Add("setup", "Game setup");
        if (controller.Definition.HasAchievements) pages.Add("achievements", "Achievements");

        rail.Children.Add(Text(controller.Definition.Title, 22, accent));
        rail.Children.Add(Text("COMPANION", 12, muted));
        foreach (var pair in pages) { var id = pair.Key; rail.Children.Add(Button(pair.Value, "nav:" + id, () => Navigate(id))); }
        Grid.SetRowSpan(rail, 4); layout.Children.Add(rail);
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(20, 16) };
        header.Children.Add(Text(controller.Definition.Title, 22));
        header.Children.Add(profiles);
        Place(header, 1, 0);
        profiles.ItemsSource = controller.Profiles.Select(profile => new ProfileItem(profile)).ToArray();
        profiles.SelectedIndex = 0;
        profiles.SelectionChanged += (_, _) =>
        {
            if (changingProfile || profiles.SelectedItem is not ProfileItem item) return;
            Try(() => { if (editErrors.Count > 0) throw new InvalidOperationException("Correct or revert invalid input before switching profiles."); controller.SelectProfile(item.Profile.Id); Navigate(current); });
            changingProfile = true;
            profiles.SelectedIndex = controller.Profiles.ToList().FindIndex(profile => profile.Id == controller.Profile.Id);
            changingProfile = false;
        };
        navigation.ItemsSource = pages.Select(pair => new PageItem(pair.Key, pair.Value)).ToArray();
        navigation.SelectedIndex = 0;
        navigation.SelectionChanged += (_, _) => { if (navigation.SelectedItem is PageItem item && item.Id != current) Navigate(item.Id); };
        navigation.Margin = new Thickness(20, 0);
        Place(navigation, 1, 1);
        Place(new ScrollViewer { Content = page, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, 1, 2);
        footer.Children.Add(status);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        apply = Button("Apply settings", "apply", () => Try(() => { if (editErrors.Count > 0) throw new InvalidOperationException(editErrors.Values.First()); controller.Apply(); Navigate(current); Notify("Settings applied."); }));
        revert = Button("Revert", "revert", () => Try(() => { controller.Reload(); editErrors.Clear(); Navigate(current); Notify("Saved settings reloaded."); }));
        actions.Children.Add(revert); actions.Children.Add(apply); footer.Children.Add(actions);
        Place(footer, 1, 3);
        Content = layout;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Navigate("play");
    }

    static IBrush Brush(string value, string fallback) { try { return new SolidColorBrush(Color.Parse(value)); } catch (FormatException) { return new SolidColorBrush(Color.Parse(fallback)); } }
    void Place(Control control, int column, int row) { Grid.SetColumn(control, column); Grid.SetRow(control, row); layout.Children.Add(control); }
    TextBlock Text(string value, double size = 14, IBrush? color = null) => new() { Text = value, FontSize = size, Foreground = color ?? foreground, TextWrapping = TextWrapping.Wrap };
    Button Button(string text, string name, Action click)
    {
        var button = new Button { Name = name, Content = text, Padding = new Thickness(16, 10), Margin = new Thickness(0, 0, 8, 4), MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Center };
        button.Click += (_, _) => click(); return button;
    }

    void UpdateResponsiveLayout()
    {
        var compact = Bounds.Width < 700;
        rail.IsVisible = !compact; navigation.IsVisible = compact;
        layout.ColumnDefinitions[0].Width = new GridLength(compact ? 0 : 220);
    }

    public void Navigate(string id)
    {
        if (!pages.ContainsKey(id)) throw new ArgumentException("Unknown launcher page.");
        current = id;
        navigation.SelectedIndex = pages.Keys.ToList().IndexOf(id);
        foreach (var button in rail.Children.OfType<Button>())
        {
            button.Background = button.Name == "nav:" + id ? accent : surface;
            button.Foreground = button.Name == "nav:" + id ? background : foreground;
        }
        page.Children.Clear(); editControls.Clear(); play = resume = null;
        try
        {
            if (id == "play") BuildPlay();
            else if (id == "achievements") BuildAchievements();
            else if (id == "setup") BuildSetup();
            else BuildOptions(id[8..]);
        }
        catch (Exception error) { page.Children.Add(Text("Could not open this page", 22)); page.Children.Add(Text(error.Message)); Notify(error.Message); }
        Refresh(); UpdateResponsiveLayout();
    }

    void BuildPlay()
    {
        page.Children.Add(Text("Ready to play", 28));
        page.Children.Add(Text(controller.Definition.Description, 14, muted));
        page.Children.Add(Text(controller.Profile.Title, 18, accent));
        page.Children.Add(Text(controller.GetReadiness().Summary));
        var actions = new WrapPanel();
        play = Button("Play", "play", () => _ = LaunchAsync(false)); play.Background = accent; play.Foreground = background;
        actions.Children.Add(play);
        if (controller.Definition.CanResume) { resume = Button("Resume quick save", "resume", () => _ = LaunchAsync(true)); actions.Children.Add(resume); }
        page.Children.Add(actions);
        foreach (var pair in pages.Where(pair => pair.Key != "play"))
        {
            var id = pair.Key;
            page.Children.Add(Button(pair.Value, "open:" + id, () => Navigate(id)));
        }
    }

    void BuildOptions(string section)
    {
        page.Children.Add(Text(section, 26));
        foreach (var option in controller.Definition.Options.Where(option => option.Section == section))
        {
            var value = controller.Snapshot.Values[option.Id];
            var card = new StackPanel { Spacing = 8 };
            card.Children.Add(Text(option.Label, 16));
            if (option.Help.Length > 0) card.Children.Add(Text(option.Help, 13, muted));
            Control input;
            if (option.Kind == OptionKind.Boolean)
            {
                var control = new CheckBox { Content = "Enabled", IsChecked = value.GetBoolean() };
                control.IsCheckedChanged += (_, _) => Edit(option.Id, OptionValue.From(control.IsChecked == true)); input = control;
            }
            else if (option.Kind == OptionKind.Integer)
            {
                var control = new NumericUpDown { Minimum = option.Minimum ?? int.MinValue, Maximum = option.Maximum ?? int.MaxValue, Value = value.GetInt32(), Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch };
                control.ValueChanged += (_, _) =>
                {
                    if (control.Value is not { } number || decimal.Truncate(number) != number) { editErrors[option.Id] = option.Label + ": enter a whole number."; Notify(editErrors[option.Id]); Refresh(); }
                    else Edit(option.Id, OptionValue.From((int)number));
                }; input = control;
            }
            else if (option.Kind == OptionKind.Choice)
            {
                var choices = option.Choices!.Select(choice => new ChoiceDisplay(choice)).ToArray();
                var control = new ComboBox { ItemsSource = choices, SelectedIndex = Array.FindIndex(choices, choice => choice.Item.Value == value.GetString()), HorizontalAlignment = HorizontalAlignment.Stretch };
                control.SelectionChanged += (_, _) => { if (control.SelectedItem is ChoiceDisplay choice) Edit(option.Id, OptionValue.From(choice.Item.Value)); }; input = control;
            }
            else
            {
                var control = new TextBox { Text = value.GetString(), MaxLength = 16384 };
                control.TextChanged += (_, _) => Edit(option.Id, OptionValue.From(control.Text ?? "")); input = control;
            }
            input.Name = "option:" + option.Id; editControls.Add(input); card.Children.Add(input);
            page.Children.Add(new Border { Background = surface, Padding = new Thickness(16), CornerRadius = new CornerRadius(4), Child = card });
        }
    }

    void Edit(string id, JsonElement value)
    {
        try { controller.SetOption(id, value); editErrors.Remove(id); Notify("Unapplied changes."); }
        catch (Exception error) { editErrors[id] = error.Message; Notify(error.Message); }
        Refresh();
    }

    void BuildAchievements()
    {
        var snapshot = controller.ReadAchievements();
        page.Children.Add(Text(snapshot.Title, 26));
        page.Children.Add(Text($"{snapshot.Entries.Count(entry => entry.UnlockedAt > 0)} of {snapshot.Entries.Count} earned", 16, accent));
        page.Children.Add(Button("Refresh progress", "refresh-achievements", () => Navigate("achievements")));
        foreach (var entry in snapshot.Entries)
        {
            var words = new StackPanel { Spacing = 8 };
            words.Children.Add(Text((entry.UnlockedAt > 0 ? "★  " : "○  ") + entry.Title, 18, entry.UnlockedAt > 0 ? accent : foreground));
            words.Children.Add(Text(entry.Description, 14, muted));
            words.Children.Add(Text(entry.UnlockedAt > 0 ? "Earned" : entry.Progress > 0 ? $"{entry.Progress} / {entry.Target}" : "Locked", 13, muted));
            page.Children.Add(new Border { Background = surface, Padding = new Thickness(16), Child = words, CornerRadius = new CornerRadius(4) });
        }
    }

    void BuildSetup()
    {
        page.Children.Add(Text("Game setup", 26)); page.Children.Add(Text(controller.GetReadiness().Summary));
        var button = Button("Run setup", "setup-start", () => _ = SetupAsync()); button.IsEnabled = !busy && !controller.HasChanges && editErrors.Count == 0; page.Children.Add(button);
        if (setup is not null) page.Children.Add(Button("Cancel setup", "setup-cancel", () => setup.Cancel()));
    }

    async Task SetupAsync()
    {
        if (busy) return;
        setup = new CancellationTokenSource(); busy = true; Navigate("setup");
        try
        {
            await controller.SetupAsync(new Progress<OperationProgress>(value => Dispatcher.UIThread.Post(() => Notify(value.Detail))), setup.Token);
            controller.Reload(); Notify("Setup complete.");
        }
        catch (OperationCanceledException) { Notify("Setup cancelled."); }
        catch (Exception error) { Notify(error.Message); }
        finally { setup.Dispose(); setup = null; busy = false; Navigate(current); }
    }

    async Task LaunchAsync(bool isResume)
    {
        if (busy) return;
        try
        {
            var plan = controller.BuildLaunchPlan(isResume);
            busy = true; Refresh(); Notify("Game running.");
            await launches.RunAsync(plan);
            controller.Reload(); Notify("Game closed.");
        }
        catch (Exception error) { Notify(error.Message); }
        finally { busy = false; Navigate(current); }
    }

    void Refresh()
    {
        profiles.IsEnabled = !busy;
        foreach (var control in editControls) control.IsEnabled = !busy;
        if (apply is not null) apply.IsEnabled = !busy && controller.HasChanges && editErrors.Count == 0;
        if (revert is not null) revert.IsEnabled = !busy && (controller.HasChanges || editErrors.Count > 0);
        try
        {
            var ready = !busy && !controller.HasChanges && editErrors.Count == 0 && controller.GetReadiness().Ready;
            if (play is not null) play.IsEnabled = ready;
            if (resume is not null) resume.IsEnabled = ready;
        }
        catch (Exception error) { if (play is not null) play.IsEnabled = false; if (resume is not null) resume.IsEnabled = false; Notify(error.Message); }
    }
    void Try(Action action) { try { action(); } catch (Exception error) { Notify(error.Message); } finally { Refresh(); } }
    void Notify(string message) { status.Text = message; status.Foreground = muted; }
    public void RequestClose() { if (setup is not null) { setup.Cancel(); Notify("Cancelling setup; close again when it finishes."); } else Notify("Apply or revert your changes before closing."); }
    record ProfileItem(ProfileDescriptor Profile) { public override string ToString() => Profile.Title; }
    record PageItem(string Id, string Title) { public override string ToString() => Title; }
    record ChoiceDisplay(ChoiceItem Item) { public override string ToString() => Item.Label; }
}
