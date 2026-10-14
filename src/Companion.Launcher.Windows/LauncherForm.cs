using System.Text.Json;
using Companion.Launcher.Core;

namespace Companion.Launcher.Windows;

/// <summary>A reusable launcher window. All game-specific behavior is supplied by its controller.</summary>
public sealed class LauncherForm : Form
{
    private readonly LauncherController controller;
    private readonly LauncherPalette palette;
    private readonly FlowLayoutPanel navigation = new();
    private readonly FlowLayoutPanel page = new();
    private readonly Label status = new();
    private readonly ComboBox profiles = new();
    private readonly Button apply;
    private readonly Button revert;
    private readonly Dictionary<string, Button> navigationButtons = new(StringComparer.Ordinal);
    private readonly ToolTip help = new() { AutoPopDelay = 15000 };
    private readonly System.Windows.Forms.Timer gameTimer = new() { Interval = 500 };
    private readonly List<RunningGame> runningGames = [];
    private readonly List<string> sections;
    private CancellationTokenSource? setupCancellation;
    private Button? playButton;
    private Button? resumeButton;
    private Label? readinessLabel;
    private string currentPage = "play";
    private Exception? pageError;
    private bool syncingProfile;

    public LauncherForm(LauncherController controller)
    {
        this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        palette = new LauncherPalette(controller.Definition.Theme);
        sections = controller.Definition.Options.Select(option => option.Section).Distinct(StringComparer.Ordinal).ToList();
        Text = controller.Definition.Title.Contains("Companion", StringComparison.OrdinalIgnoreCase)
            ? controller.Definition.Title : controller.Definition.Title + " Companion";
        BackColor = palette.Background;
        ForeColor = palette.Text;
        Font = LauncherPalette.Font();
        ClientSize = new Size(1120, 800);
        MinimumSize = new Size(960, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        Controls.Add(layout);

        navigation.Dock = DockStyle.Fill;
        navigation.FlowDirection = FlowDirection.TopDown;
        navigation.WrapContents = false;
        navigation.AutoScroll = true;
        navigation.BackColor = palette.Rail;
        navigation.Padding = new Padding(22, 28, 14, 20);
        layout.Controls.Add(navigation, 0, 0);
        layout.SetRowSpan(navigation, 3);
        var brand = palette.Label(controller.Definition.Title, 16, palette.Accent);
        brand.Font = LauncherPalette.Font(16, FontStyle.Bold);
        brand.MaximumSize = new Size(170, 0);
        navigation.Controls.Add(brand);
        navigation.Controls.Add(palette.Label("COMPANION", 10, palette.Muted));
        navigation.Controls.Add(new Panel { Width = 170, Height = 22, Margin = Padding.Empty });
        AddNavigation("play", "Play");
        foreach (var section in sections) AddNavigation("section:" + section, section);
        if (controller.Definition.HasAchievements) AddNavigation("achievements", "Achievements");
        if (controller.Definition.HasSetup) AddNavigation("setup", "Game setup");

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(32, 20, 28, 8), BackColor = palette.Background };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(header, 1, 0);
        var label = palette.Label("YOUR GAME", 9, palette.Muted);
        label.Margin = Padding.Empty;
        header.Controls.Add(label, 0, 0);
        var gameTitle = palette.Label(controller.Definition.Title, 14);
        gameTitle.Font = LauncherPalette.Font(14, FontStyle.Bold);
        gameTitle.AutoSize = false;
        gameTitle.Dock = DockStyle.Fill;
        gameTitle.AutoEllipsis = true;
        header.Controls.Add(gameTitle, 0, 1);
        var profileLabel = palette.Label("PROFILE", 9, palette.Muted);
        profileLabel.Margin = new Padding(16, 0, 0, 0);
        header.Controls.Add(profileLabel, 1, 0);
        ConfigureChoice(profiles);
        profiles.Dock = DockStyle.Top;
        profiles.Margin = new Padding(16, 0, 0, 0);
        profiles.AccessibleName = "Game profile";
        foreach (var profile in controller.Profiles) profiles.Items.Add(new ProfileItem(profile));
        SynchronizeProfile();
        profiles.SelectedIndexChanged += ProfileChanged;
        header.Controls.Add(profiles, 1, 1);

        page.Dock = DockStyle.Fill;
        page.FlowDirection = FlowDirection.TopDown;
        page.WrapContents = false;
        page.AutoScroll = true;
        page.Padding = new Padding(32, 14, 28, 20);
        page.SizeChanged += (_, _) => ResizePage();
        layout.Controls.Add(page, 1, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(32, 12, 28, 12), BackColor = palette.Rail };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));
        status.Dock = DockStyle.Fill;
        status.ForeColor = palette.Muted;
        status.Font = LauncherPalette.Font(10);
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        footer.Controls.Add(status, 0, 0);
        revert = palette.Button("Revert", Revert, width: 104);
        apply = palette.Button("Apply settings", Apply, primary: true, width: 164);
        footer.Controls.Add(revert, 1, 0);
        footer.Controls.Add(apply, 2, 0);
        layout.Controls.Add(footer, 1, 2);

        ShowPage("play");
        Notify("Your saved settings are loaded.");
        gameTimer.Tick += (_, _) => RefreshRunningGames();
        gameTimer.Start();
        FormClosing += HandleClosing;
    }

    private void AddNavigation(string key, string title)
    {
        var button = palette.Button(title, () => ShowPage(key), width: 172);
        button.Height = 48;
        button.Margin = new Padding(0, 0, 0, 6);
        button.AccessibleName = title;
        navigationButtons.Add(key, button);
        navigation.Controls.Add(button);
    }

    private void ShowPage(string key)
    {
        currentPage = key;
        pageError = null;
        page.SuspendLayout();
        foreach (var control in page.Controls.Cast<Control>().ToArray()) control.Dispose();
        page.Controls.Clear();
        playButton = resumeButton = null;
        readinessLabel = null;
        foreach (var entry in navigationButtons)
        {
            entry.Value.BackColor = entry.Key == key ? palette.Surface : palette.Rail;
            entry.Value.ForeColor = entry.Key == key ? palette.Accent : palette.Muted;
            entry.Value.FlatAppearance.BorderColor = entry.Key == key ? palette.Line : palette.Rail;
        }
        try
        {
            if (key == "play") BuildPlayPage();
            else if (key == "achievements") BuildAchievementsPage();
            else if (key == "setup") BuildSetupPage();
            else BuildOptionsPage(key[8..]);
        }
        catch (Exception error) { ShowPageError(error); }
        ResizePage();
        page.ResumeLayout(true);
        page.AutoScrollPosition = Point.Empty;
        RefreshActions();
    }

    private void Heading(string title, string description)
    {
        var heading = palette.Label(title, 25);
        heading.Font = LauncherPalette.Font(25, FontStyle.Bold);
        heading.Margin = new Padding(0, 0, 0, 10);
        page.Controls.Add(heading);
        if (!string.IsNullOrWhiteSpace(description)) page.Controls.Add(palette.Label(description, 11, palette.Muted));
        page.Controls.Add(new Panel { Height = 14, Margin = Padding.Empty });
    }

    private void BuildPlayPage()
    {
        Heading("Ready for another adventure?", controller.Definition.Description);
        var profileTitle = palette.Label(controller.Profile.Title, 14, palette.Accent);
        profileTitle.Font = LauncherPalette.Font(14, FontStyle.Bold);
        page.Controls.Add(profileTitle);
        readinessLabel = palette.Label("", 12);
        page.Controls.Add(readinessLabel);
        var actions = Actions();
        playButton = palette.Button("Play", () => Launch(false), primary: true);
        actions.Controls.Add(playButton);
        if (controller.Definition.CanResume)
        {
            resumeButton = palette.Button("Resume quick save", () => Launch(true), width: 208);
            actions.Controls.Add(resumeButton);
        }
        var profileDetails = new[]
        {
            string.IsNullOrWhiteSpace(controller.Profile.EditionId) ? null : "Edition: " + controller.Profile.EditionId,
            string.IsNullOrWhiteSpace(controller.Profile.SeedId) ? null : "Seed: " + controller.Profile.SeedId
        }.Where(value => value is not null);
        page.Controls.Add(palette.Label(string.Join("  ·  ", profileDetails), 10, palette.Muted));
        if (sections.Count > 0)
        {
            page.Controls.Add(new Panel { Height = 20, Margin = Padding.Empty });
            page.Controls.Add(palette.Label("Make it yours", 14));
            foreach (var section in sections)
            {
                var options = controller.Definition.Options.Where(option => option.Section == section).ToList();
                var card = new Panel { Height = 84, Margin = new Padding(0, 0, 0, 10), BackColor = palette.Surface, Padding = new Padding(18, 12, 18, 12) };
                var open = palette.Button("Open", () => ShowPage("section:" + section), width: 90);
                open.Dock = DockStyle.Right;
                var name = palette.Label(section, 12);
                name.Font = LauncherPalette.Font(12, FontStyle.Bold);
                name.Dock = DockStyle.Top;
                var summary = palette.Label(string.Join(" · ", options.Take(3).Select(option => option.Label)), 10, palette.Muted);
                summary.AutoSize = false;
                summary.AutoEllipsis = true;
                summary.Dock = DockStyle.Fill;
                var words = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 14, 0) };
                words.Controls.Add(summary);
                words.Controls.Add(name);
                card.Controls.Add(words);
                card.Controls.Add(open);
                page.Controls.Add(card);
            }
        }
    }

    private void BuildOptionsPage(string section)
    {
        Heading(section, "Choose your settings, then Apply when you are ready.");
        foreach (var option in controller.Definition.Options.Where(option => option.Section == section))
        {
            var value = controller.Snapshot.Values.TryGetValue(option.Id, out var saved) ? saved : option.DefaultValue;
            Control input;
            switch (option.Kind)
            {
                case OptionKind.Boolean:
                    var checkbox = new CheckBox { Text = "Enabled", AutoSize = true, Checked = value.ValueKind == JsonValueKind.True, ForeColor = palette.Text, Height = 34 };
                    checkbox.CheckedChanged += (_, _) => SetOption(option.Id, JsonSerializer.SerializeToElement(checkbox.Checked));
                    input = checkbox;
                    break;
                case OptionKind.Integer:
                    var number = new NumericUpDown { Minimum = option.Minimum ?? int.MinValue, Maximum = option.Maximum ?? int.MaxValue, BackColor = palette.Surface, ForeColor = palette.Text, BorderStyle = BorderStyle.FixedSingle };
                    number.Value = Math.Clamp(value.GetInt32(), number.Minimum, number.Maximum);
                    number.ValueChanged += (_, _) => SetOption(option.Id, JsonSerializer.SerializeToElement((int)number.Value));
                    input = number;
                    break;
                case OptionKind.Choice:
                    var choice = new ComboBox();
                    ConfigureChoice(choice);
                    var choices = option.Choices ?? throw new InvalidOperationException("This choice has no values: " + option.Label);
                    foreach (var item in choices) choice.Items.Add(new OptionItem(item));
                    var selected = choices.Select((item, index) => (item, index)).FirstOrDefault(pair => pair.item.Value == value.GetString());
                    choice.SelectedIndex = selected.item is null ? -1 : selected.index;
                    choice.SelectedIndexChanged += (_, _) =>
                    {
                        if (choice.SelectedItem is OptionItem item) SetOption(option.Id, JsonSerializer.SerializeToElement(item.Choice.Value));
                    };
                    input = choice;
                    break;
                case OptionKind.Text:
                    var text = new TextBox { Text = value.GetString() ?? "", BackColor = palette.Surface, ForeColor = palette.Text, BorderStyle = BorderStyle.FixedSingle };
                    text.TextChanged += (_, _) => SetOption(option.Id, JsonSerializer.SerializeToElement(text.Text));
                    input = text;
                    break;
                default: throw new InvalidOperationException("Unsupported option type: " + option.Kind);
            }
            input.Name = "option:" + option.Id;
            input.AccessibleName = option.Label;
            input.AccessibleDescription = option.Help;
            AddOptionRow(option, input);
        }
    }

    private void AddOptionRow(OptionDefinition option, Control input)
    {
        var row = new TableLayoutPanel { Height = 100, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(16, 12, 16, 12), BackColor = palette.Surface };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var name = palette.Label(option.Label, 11);
        name.Font = LauncherPalette.Font(11, FontStyle.Bold);
        name.Margin = Padding.Empty;
        row.Controls.Add(name, 0, 0);
        var description = palette.Label(option.Help ?? "", 10, palette.Muted);
        description.Margin = Padding.Empty;
        row.Controls.Add(description, 0, 1);
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(18, 4, 0, 0);
        row.Controls.Add(input, 1, 0);
        row.SetRowSpan(input, 2);
        row.SizeChanged += (_, _) =>
        {
            var width = Math.Max(100, (int)((row.ClientSize.Width - row.Padding.Horizontal) * .6) - 16);
            name.MaximumSize = description.MaximumSize = new Size(width, 0);
            var measured = TextRenderer.MeasureText(description.Text, description.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
            row.Height = Math.Max(100, measured.Height + 58);
        };
        help.SetToolTip(input, option.Help);
        page.Controls.Add(row);
    }

    private void BuildAchievementsPage()
    {
        var snapshot = controller.ReadAchievements();
        Heading(snapshot.Title, "Your progress in this profile.");
        var earned = snapshot.Entries.Count(entry => entry.UnlockedAt != 0);
        page.Controls.Add(palette.Label($"{earned} of {snapshot.Entries.Count} earned", 12, palette.Accent));
        Actions().Controls.Add(palette.Button("Refresh progress", () => ShowPage("achievements")));
        if (snapshot.Entries.Count == 0) page.Controls.Add(palette.Label("No achievements are available for this profile yet.", 11, palette.Muted));
        foreach (var entry in snapshot.Entries)
        {
            var earnedEntry = entry.UnlockedAt != 0;
            var row = new TableLayoutPanel { Height = 106, ColumnCount = 2, RowCount = 3, BackColor = palette.Surface, Margin = new Padding(0, 0, 0, 10), Padding = new Padding(15, 12, 15, 12) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 43));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            var badge = palette.Label(earnedEntry ? "★" : "○", 23, earnedEntry ? palette.Accent : palette.Muted);
            badge.Margin = Padding.Empty;
            row.Controls.Add(badge, 0, 0);
            row.SetRowSpan(badge, 3);
            var title = palette.Label(entry.Title, 12);
            title.Font = LauncherPalette.Font(12, FontStyle.Bold);
            title.Margin = Padding.Empty;
            row.Controls.Add(title, 1, 0);
            var description = palette.Label(entry.Description, 10, palette.Muted);
            description.Margin = Padding.Empty;
            row.Controls.Add(description, 1, 1);
            var detail = earnedEntry ? "Earned" : entry.Target > 1 ? $"{Math.Min(entry.Progress, entry.Target)} / {entry.Target}" : "Locked";
            var progress = palette.Label(detail, 9, earnedEntry ? palette.Success : palette.Muted);
            progress.Margin = Padding.Empty;
            row.Controls.Add(progress, 1, 2);
            row.SizeChanged += (_, _) =>
            {
                var width = Math.Max(100, row.Width - row.Padding.Horizontal - 55);
                title.MaximumSize = description.MaximumSize = new Size(width, 0);
                var measured = TextRenderer.MeasureText(description.Text, description.Font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak);
                row.Height = Math.Max(106, measured.Height + 72);
            };
            page.Controls.Add(row);
        }
    }

    private void BuildSetupPage()
    {
        Heading("Game setup", "Prepare the game data for your selected profile.");
        var readiness = controller.GetReadiness();
        page.Controls.Add(palette.Label(readiness.Summary, 12, readiness.Ready ? palette.Success : palette.Muted));
        var setupButton = palette.Button("Run setup", () => _ = RunSetupAsync(), primary: true);
        setupButton.Enabled = setupCancellation is null && runningGames.Count == 0 && !controller.HasChanges;
        Actions().Controls.Add(setupButton);
        if (setupCancellation is not null) page.Controls.Add(palette.Label("Setup is running. You can cancel below.", 11, palette.Muted));
    }

    private async Task RunSetupAsync()
    {
        if (setupCancellation is not null) return;
        if (controller.HasChanges) { Notify("Apply or revert your changes before running setup.", true); return; }
        setupCancellation = new CancellationTokenSource();
        var cancellation = setupCancellation;
        ShowPage("setup");
        var detail = palette.Label("Starting setup…", 11);
        var progressBar = new ProgressBar { Height = 14, Style = ProgressBarStyle.Marquee, Margin = new Padding(0, 8, 0, 16) };
        page.Controls.Add(detail);
        page.Controls.Add(progressBar);
        Actions().Controls.Add(palette.Button("Cancel setup", () => cancellation.Cancel()));
        ResizePage();
        var progress = new Progress<OperationProgress>(value =>
        {
            Notify(value.Detail);
            if (detail.IsDisposed) return;
            detail.Text = value.Detail;
            progressBar.Style = value.Fraction is null ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (value.Fraction is not null) progressBar.Value = (int)(Math.Clamp(value.Fraction.Value, 0, 1) * 100);
        });
        try
        {
            await controller.SetupAsync(progress, cancellation.Token);
            controller.Reload();
            Notify("Setup complete.");
        }
        catch (OperationCanceledException) { Notify("Setup cancelled."); }
        catch (Exception error) { Notify(error.Message, true); }
        finally
        {
            setupCancellation = null;
            cancellation.Dispose();
            if (!IsDisposed) ShowPage(currentPage);
        }
    }

    private FlowLayoutPanel Actions()
    {
        var actions = new FlowLayoutPanel { Height = 60, WrapContents = true, Margin = new Padding(0, 6, 0, 12), AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        page.Controls.Add(actions);
        return actions;
    }

    private void ConfigureChoice(ComboBox choice)
    {
        choice.DropDownStyle = ComboBoxStyle.DropDownList;
        choice.DrawMode = DrawMode.OwnerDrawFixed;
        choice.ItemHeight = 25;
        choice.BackColor = palette.Surface;
        choice.ForeColor = palette.Text;
        choice.FlatStyle = FlatStyle.Flat;
        choice.DrawItem += (_, args) =>
        {
            using var brush = new SolidBrush(palette.Surface);
            args.Graphics.FillRectangle(brush, args.Bounds);
            if (args.Index >= 0) TextRenderer.DrawText(args.Graphics, choice.Items[args.Index]!.ToString(), choice.Font,
                new Rectangle(args.Bounds.X + 6, args.Bounds.Y, args.Bounds.Width - 12, args.Bounds.Height),
                (args.State & DrawItemState.Selected) != 0 ? palette.Accent : palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            args.DrawFocusRectangle();
        };
    }

    private void SynchronizeProfile()
    {
        syncingProfile = true;
        profiles.SelectedIndex = profiles.Items.Cast<ProfileItem>().Select((item, index) => (item, index))
            .First(pair => pair.item.Profile.Id == controller.Profile.Id).index;
        syncingProfile = false;
    }

    private void ProfileChanged(object? sender, EventArgs args)
    {
        if (syncingProfile || profiles.SelectedItem is not ProfileItem selection) return;
        if (selection.Profile.Id == controller.Profile.Id) return;
        if (controller.HasChanges)
        {
            SynchronizeProfile();
            Notify("Apply or revert your changes before switching profiles.", true);
            return;
        }
        try
        {
            controller.SelectProfile(selection.Profile.Id);
            ShowPage(currentPage);
            Notify("Loaded " + controller.Profile.Title + ".");
        }
        catch (Exception error) { SynchronizeProfile(); Notify(error.Message, true); }
    }

    private void SetOption(string id, JsonElement value)
    {
        try { controller.SetOption(id, value); Notify(controller.HasChanges ? "You have unapplied changes." : "Your settings match the saved profile."); }
        catch (Exception error) { Notify(error.Message, true); }
        RefreshActions();
    }

    private void Apply()
    {
        try { controller.Apply(); Notify("Settings applied."); }
        catch (Exception error) { Notify(error.Message, true); }
        RefreshActions();
    }

    private void Revert()
    {
        try { controller.Reload(); ShowPage(currentPage); Notify("Your saved settings are loaded."); }
        catch (Exception error) { Notify(error.Message, true); }
        RefreshActions();
    }

    private void Launch(bool resume)
    {
        try
        {
            if (controller.HasChanges) { Notify("Apply or revert your changes before starting the game.", true); return; }
            var plan = controller.BuildLaunchPlan(resume);
            var running = ProcessGameRuntime.Start(plan);
            runningGames.Add(running);
            Notify("Game started.");
        }
        catch (Exception error) { Notify(error.Message, true); }
        RefreshActions();
    }

    private void RefreshRunningGames()
    {
        var finished = false;
        foreach (var running in runningGames.Where(running => running.HasExited).ToArray())
        {
            runningGames.Remove(running);
            running.Dispose();
            finished = true;
        }
        if (finished)
        {
            if (currentPage is "play" or "achievements") ShowPage(currentPage);
            Notify("Game closed. Your progress is ready to view.");
        }
        RefreshActions();
    }

    private void RefreshActions()
    {
        var busy = setupCancellation is not null;
        apply.Enabled = !busy && controller.HasChanges;
        revert.Enabled = !busy && controller.HasChanges;
        profiles.Enabled = !busy && runningGames.Count == 0;
        foreach (var entry in navigationButtons) entry.Value.Enabled = !busy;
        try
        {
            var readiness = controller.GetReadiness();
            if (readinessLabel is not null)
            {
                readinessLabel.Text = readiness.Summary;
                readinessLabel.ForeColor = readiness.Ready ? palette.Success : palette.Muted;
            }
            var launchable = readiness.Ready && !busy && !controller.HasChanges && runningGames.Count == 0;
            if (playButton is not null) playButton.Enabled = launchable;
            if (resumeButton is not null) resumeButton.Enabled = launchable;
        }
        catch (Exception error)
        {
            if (playButton is not null) playButton.Enabled = false;
            if (resumeButton is not null) resumeButton.Enabled = false;
            if (readinessLabel is not null) { readinessLabel.Text = error.Message; readinessLabel.ForeColor = palette.Error; }
        }
    }

    private void Notify(string message, bool error = false)
    {
        status.Text = message;
        status.ForeColor = error ? palette.Error : palette.Muted;
        help.SetToolTip(status, message);
    }

    private void ShowPageError(Exception error)
    {
        pageError = error;
        Heading("Could not open this page", error.Message);
        Notify(error.Message, true);
    }

    private void ResizePage()
    {
        var width = Math.Max(300, page.ClientSize.Width - page.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
        foreach (Control control in page.Controls)
        {
            if (control is Label label) label.MaximumSize = new Size(width, 0);
            else control.Width = width;
        }
    }

    private void HandleClosing(object? sender, FormClosingEventArgs args)
    {
        if (setupCancellation is not null)
        {
            setupCancellation.Cancel();
            args.Cancel = true;
            Notify("Cancelling setup. Close the launcher when it has finished.");
            return;
        }
        if (controller.HasChanges && MessageBox.Show(this, "Close without applying your changes?", "Unapplied settings",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) args.Cancel = true;
    }

    /// <summary>Renders every declared page without launching the game or applying settings.</summary>
    public IReadOnlyList<string> RenderSmokeTest(string outputDirectory)
    {
        outputDirectory = Path.GetFullPath(outputDirectory);
        if (Directory.Exists(outputDirectory) || File.Exists(outputDirectory)) throw new IOException("Smoke-test output must be a fresh directory.");
        Directory.CreateDirectory(outputDirectory);
        var images = new List<string>();
        Show();
        try
        {
            var draftChecks = VerifyDraftControls();
            var index = 0;
            foreach (var key in navigationButtons.Keys)
            {
                ShowPage(key);
                if (pageError is not null) throw new InvalidOperationException("Could not render " + key + ".", pageError);
                PerformLayout();
                Application.DoEvents();
                using var bitmap = new Bitmap(Width, Height);
                DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size));
                var image = Path.Combine(outputDirectory, $"{index++:00}-{SafeName(key)}.png");
                bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
                images.Add(image);
            }
            File.WriteAllText(Path.Combine(outputDirectory, "smoke-test.json"), JsonSerializer.Serialize(new
            {
                gameId = controller.Definition.Id,
                pageCount = images.Count,
                images,
                clientWidth = ClientSize.Width,
                clientHeight = ClientSize.Height,
                gameLaunched = false,
                settingsApplied = false,
                draftChecks
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Hide(); }
        return images;
    }

    private object VerifyDraftControls()
    {
        var option = controller.Definition.Options.FirstOrDefault(value => value.Kind == OptionKind.Boolean);
        if (option is null) return new { tested = false, reason = "No boolean option was declared." };
        if (controller.HasChanges) throw new InvalidOperationException("Smoke testing requires a clean saved profile.");
        var originalProfile = controller.Profile.Id;
        var originalRevision = controller.Snapshot.Revision;
        ShowPage("section:" + option.Section);
        var checkbox = page.Controls.Find("option:" + option.Id, true).OfType<CheckBox>().Single();
        var edited = !checkbox.Checked;
        checkbox.Checked = edited;
        if (!controller.HasChanges) throw new InvalidOperationException("Option edits did not create a draft.");
        ShowPage("play");
        if (playButton is null || playButton.Enabled || !apply.Enabled || !revert.Enabled)
            throw new InvalidOperationException("Unapplied changes did not disable Play or enable Apply/Revert.");
        var profileSwitchTested = profiles.Items.Count > 1;
        if (profileSwitchTested)
        {
            profiles.SelectedIndex = (profiles.SelectedIndex + 1) % profiles.Items.Count;
            if (controller.Profile.Id != originalProfile || ((ProfileItem)profiles.SelectedItem!).Profile.Id != originalProfile)
                throw new InvalidOperationException("A dirty profile switch discarded a draft.");
        }
        ShowPage("section:" + option.Section);
        checkbox = page.Controls.Find("option:" + option.Id, true).OfType<CheckBox>().Single();
        if (checkbox.Checked != edited) throw new InvalidOperationException("Navigation discarded an unapplied option.");
        if (controller.Snapshot.Revision != originalRevision) throw new InvalidOperationException("A draft edit wrote the saved revision.");
        Revert();
        if (controller.HasChanges) throw new InvalidOperationException("Revert did not restore saved options.");
        return new { tested = true, navigationPreservedDraft = true, dirtyPlayDisabled = true, profileSwitchTested, revertRestoredSaved = true };
    }

    private static string SafeName(string value) => string.Concat(value.Select(character => char.IsLetterOrDigit(character) ? character : '-'));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            gameTimer.Stop();
            gameTimer.Dispose();
            help.Dispose();
            setupCancellation?.Cancel();
            foreach (var running in runningGames) running.Dispose();
            runningGames.Clear();
        }
        base.Dispose(disposing);
    }

    private sealed record ProfileItem(ProfileDescriptor Profile) { public override string ToString() => Profile.Title; }
    private sealed record OptionItem(ChoiceItem Choice) { public override string ToString() => Choice.Label; }
}
