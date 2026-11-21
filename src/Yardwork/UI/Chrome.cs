using System.Drawing.Drawing2D;
using System.Drawing.Text;
using Yardwork.Native;
using Yardwork.UI.Controls;

namespace Yardwork.UI;

/// <summary>Top banner: logo, title, decorative accent lines and the window buttons. Drag it to move the window.</summary>
internal sealed class HeaderBar : PaintedControl
{
    private readonly MainForm _form;
    private readonly GlyphButton _minimize, _maximize, _close;

    public HeaderBar(MainForm form)
    {
        _form = form;
        Height = Ui.S(96);
        BackColor = Theme.Bg;

        _minimize = new GlyphButton(Glyphs.Minimize) { Size = Ui.S(46, 34) };
        _maximize = new GlyphButton(Glyphs.Maximize) { Size = Ui.S(46, 34) };
        _close = new GlyphButton(Glyphs.Close) { Size = Ui.S(46, 34), Danger = true, GlyphColor = Theme.AccentBright };
        _minimize.Click += (_, _) => _form.WindowState = FormWindowState.Minimized;
        _maximize.Click += (_, _) => _form.ToggleMaximize();
        _close.Click += (_, _) => _form.Close();
        Controls.AddRange([_minimize, _maximize, _close]);
    }

    public void UpdateMaximizeGlyph() =>
        _maximize.Glyph = _form.WindowState == FormWindowState.Maximized ? Glyphs.Restore : Glyphs.Maximize;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int top = Ui.S(8);
        _close.Location = new Point(Width - _close.Width - Ui.S(8), top);
        _maximize.Location = new Point(_close.Left - _maximize.Width, top);
        _minimize.Location = new Point(_maximize.Left - _minimize.Width, top);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || e.Clicks != 1) return;
        Win32.ReleaseCapture();
        Win32.SendMessage(_form.Handle, Win32.WM_NCLBUTTONDOWN, Win32.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left) _form.ToggleMaximize();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Theme.Bg);
        if (Width <= 2) return;

        float height = Height;
        var accent = Theme.Accent;

        // Soft glow towards the right.
        using (var glow = new LinearGradientBrush(new Rectangle(-1, 0, Width + 2, Height),
                   Color.FromArgb(0, accent), Color.FromArgb(42, accent), LinearGradientMode.Horizontal))
            g.FillRectangle(glow, ClientRectangle);

        // Slanted panel and accent line, echoing the window's angular style.
        float x0 = Math.Max(Ui.S(430f), Width * 0.46f);
        float slant = height * 0.7f;
        using (var panel = new GraphicsPath())
        {
            panel.AddPolygon(new[]
            {
                new PointF(x0, height),
                new PointF(x0 + slant, Ui.S(14f)),
                new PointF(x0 + slant + Ui.S(150f), Ui.S(14f)),
                new PointF(x0 + Ui.S(150f), height),
            });
            using var fill = new SolidBrush(Color.FromArgb(90, Theme.Panel));
            g.FillPath(fill, panel);
            using var edge = new Pen(Color.FromArgb(55, Theme.AccentBright));
            g.DrawPath(edge, panel);
        }

        float lineY = height * 0.5f;
        var line = new[]
        {
            new PointF(x0 + Ui.S(150f) + Ui.S(12f), height - 1),
            new PointF(x0 + Ui.S(150f) + Ui.S(12f) + (height - lineY), lineY),
            new PointF(Width - Ui.S(20f), lineY),
        };
        using (var lineBrush = new LinearGradientBrush(new PointF(line[0].X - 1, 0), new PointF(Width, 0),
                   accent, Color.FromArgb(0, accent)))
        using (var pen = new Pen(lineBrush, Ui.S(3f)) { LineJoin = LineJoin.Round })
            g.DrawLines(pen, line);

        // Logo and title.
        Logo.DrawEye(g, new RectangleF(Ui.S(22f), Ui.S(24f), Ui.S(76f), Ui.S(48f)), accent);

        const string title = "YARDWORK";
        var titleOrigin = new PointF(Ui.S(112f), Ui.S(8f));
        var titleSize = g.MeasureString(title, Theme.Title);
        using (var titleBrush = new LinearGradientBrush(new RectangleF(titleOrigin, titleSize),
                   Theme.AccentBright, accent, LinearGradientMode.Vertical))
            g.DrawString(title, Theme.Title, titleBrush, titleOrigin);

        TextRenderer.DrawText(g, "O V E R L A Y    U T I L I T Y        v1.0", Theme.Subtitle,
            new Point(Ui.S(122), Ui.S(64)), Theme.Mix(Theme.AccentBright, Theme.TextDim, 0.3f));

        using var bottom = new Pen(Theme.Border);
        g.DrawLine(bottom, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>Bottom bar: overlay status, frame rate, current target and "Always On Top".</summary>
internal sealed class StatusBar : PaintedControl
{
    private string _status = "Starting";
    private Color _statusColor = Theme.TextDim;
    private string _fps = "—";
    private string _target = "None";

    public StatusBar()
    {
        Height = Ui.S(46);
        BackColor = Theme.Bg;
        AlwaysOnTop = new DarkCheckBox("Always On Top");
        SettingsButton = new GlyphButton(Glyphs.Settings) { Size = Ui.S(38, 34), GlyphSize = 14f };
        Controls.AddRange([AlwaysOnTop, SettingsButton]);
        Theme.AccentChanged += OnAccentChanged;
        OnAccentChanged();
    }

    public DarkCheckBox AlwaysOnTop { get; }
    public GlyphButton SettingsButton { get; }

    public void SetValues(string status, Color statusColor, string fps, string target)
    {
        if (status == _status && statusColor == _statusColor && fps == _fps && target == _target) return;
        _status = status;
        _statusColor = statusColor;
        _fps = fps;
        _target = target;
        Invalidate();
    }

    private void OnAccentChanged() => SettingsButton.GlyphColor = Theme.Accent;

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        SettingsButton.Location = new Point(Width - SettingsButton.Width - Ui.S(10), (Height - SettingsButton.Height) / 2);
        AlwaysOnTop.Location = new Point(SettingsButton.Left - AlwaysOnTop.Width - Ui.S(22), (Height - AlwaysOnTop.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Theme.Bg);

        using (var top = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.4f)))
            g.DrawLine(top, 0, 0, Width, 0);

        int x = Ui.S(18);
        int dot = Ui.S(10);
        using (var brush = new SolidBrush(_statusColor))
            g.FillEllipse(brush, x, (Height - dot) / 2f, dot, dot);
        x += dot + Ui.S(14);

        x = Segment(g, x, "Status:", Theme.Text, Theme.Body);
        x = Segment(g, x + Ui.S(6), _status, _statusColor, Theme.BodySemibold);
        x = Separator(g, x + Ui.S(20));
        x = Segment(g, x + Ui.S(20), "Overlay FPS:", Theme.TextDim, Theme.Body);
        x = Segment(g, x + Ui.S(6), _fps, Theme.Text, Theme.BodySemibold);
        x = Separator(g, x + Ui.S(20));
        x = Segment(g, x + Ui.S(20), "Target:", Theme.TextDim, Theme.Body);

        int targetRight = AlwaysOnTop.Left - Ui.S(24);
        if (targetRight > x + Ui.S(40))
            Gfx.Text(g, _target, Theme.BodySemibold, new Rectangle(x + Ui.S(6), 0, targetRight - x - Ui.S(6), Height), Theme.Text);

        Separator(g, AlwaysOnTop.Left - Ui.S(12));
    }

    private int Segment(Graphics g, int x, string text, Color color, Font font)
    {
        int width = TextRenderer.MeasureText(g, text, font).Width;
        Gfx.Text(g, text, font, new Rectangle(x, 0, width + Ui.S(4), Height), color);
        return x + width;
    }

    private int Separator(Graphics g, int x)
    {
        using var pen = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.5f));
        g.DrawLine(pen, x, Ui.S(12), x, Height - Ui.S(12));
        return x;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Theme.AccentChanged -= OnAccentChanged;
        base.Dispose(disposing);
    }
}

/// <summary>Left navigation column with a right-hand divider.</summary>
internal sealed class Sidebar : Panel
{
    public Sidebar()
    {
        BackColor = Theme.Sidebar;
        Width = Ui.S(250);
        Padding = Ui.Pad(14, 16, 14, 6);
        DoubleBuffered = true;
        ResizeRedraw = true;
        Controls.Add(new SidebarFooter { Dock = DockStyle.Bottom });
    }

    public NavButton AddButton(string text, string glyph)
    {
        var button = new NavButton(text, glyph) { Dock = DockStyle.Top };
        Controls.Add(button);
        button.BringToFront();
        return button;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, Width - 1, 0, Width - 1, Height);
    }
}

internal sealed class SidebarFooter : PaintedControl
{
    public SidebarFooter()
    {
        Height = Ui.S(66);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        using (var pen = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.5f)))
            g.DrawLine(pen, 0, 0, Width, 0);

        const string brand = "YARDWORK";
        int brandWidth = TextRenderer.MeasureText(brand, Theme.BodySemibold).Width;
        int starWidth = Ui.S(22);
        int x = (Width - brandWidth - starWidth) / 2;
        Gfx.Glyph(g, Glyphs.Star, 11f, Theme.Accent, new Rectangle(x, Ui.S(10), starWidth, Ui.S(24)));
        Gfx.Text(g, brand, Theme.BodySemibold, new Rectangle(x + starWidth, Ui.S(10), brandWidth + 4, Ui.S(24)), Theme.AccentBright);
        Gfx.Text(g, "Overlay toolkit for any window.", Theme.Small, new Rectangle(0, Ui.S(34), Width, Ui.S(20)),
            Theme.TextMuted, TextFormatFlags.HorizontalCenter);
    }
}
