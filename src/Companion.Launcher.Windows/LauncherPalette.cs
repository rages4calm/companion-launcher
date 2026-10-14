using Companion.Launcher.Core;

namespace Companion.Launcher.Windows;

internal sealed class LauncherPalette
{
    public Color Background { get; }
    public Color Surface { get; }
    public Color Accent { get; }
    public Color Text { get; }
    public Color Muted { get; }
    public Color Rail { get; }
    public Color Line { get; }
    public Color Error { get; } = Color.FromArgb(255, 166, 162);
    public Color Success { get; } = Color.FromArgb(158, 219, 179);

    public LauncherPalette(LauncherTheme theme)
    {
        Background = Parse(theme.Background, "#1A182A");
        Surface = Parse(theme.Surface, "#2A263E");
        Accent = Parse(theme.Accent, "#FAD870");
        Text = Parse(theme.Text, "#F7F3FF");
        Muted = Parse(theme.Muted, "#C2B6D8");
        Rail = Blend(Background, Color.Black, .3f);
        Line = Blend(Surface, Muted, .25f);
    }

    private static Color Parse(string? value, string fallback)
    {
        try { return ColorTranslator.FromHtml(string.IsNullOrWhiteSpace(value) ? fallback : value); }
        catch (ArgumentException) { return ColorTranslator.FromHtml(fallback); }
    }

    public static Font Font(float size = 11, FontStyle style = FontStyle.Regular) => new("Segoe UI", size, style);

    public Label Label(string text, float size = 11, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Font(size),
        ForeColor = color ?? Text,
        Margin = new Padding(0, 0, 0, 8),
        UseMnemonic = false
    };

    public Button Button(string text, Action action, bool primary = false, int width = 170)
    {
        var button = new CompanionButton
        {
            Text = text,
            Size = new Size(width, 44),
            Margin = new Padding(0, 4, 12, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Rail : Text,
            Font = Font(11, FontStyle.Bold),
            Cursor = Cursors.Hand,
            UseMnemonic = false,
            DisabledText = Muted,
            DisabledBackground = Surface,
            DisabledBorder = Line
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Line;
        button.FlatAppearance.MouseOverBackColor = Blend(button.BackColor, Text, .12f);
        button.Click += (_, _) => action();
        return button;
    }

    private static Color Blend(Color source, Color target, float amount) => Color.FromArgb(
        (int)(source.R + (target.R - source.R) * amount),
        (int)(source.G + (target.G - source.G) * amount),
        (int)(source.B + (target.B - source.B) * amount));
}

internal sealed class CompanionButton : Button
{
    public Color DisabledText { get; init; }
    public Color DisabledBackground { get; init; }
    public Color DisabledBorder { get; init; }
    protected override void OnPaint(PaintEventArgs e)
    {
        var hover = Enabled && ClientRectangle.Contains(PointToClient(Cursor.Position));
        e.Graphics.Clear(!Enabled ? DisabledBackground : hover ? FlatAppearance.MouseOverBackColor : BackColor);
        using var pen = new Pen(Enabled ? FlatAppearance.BorderColor : DisabledBorder);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? ForeColor : DisabledText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(4, 4, Width - 8, Height - 8), ForeColor, BackColor);
    }
}
