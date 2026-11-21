using System.Drawing.Drawing2D;

namespace Yardwork.UI.Controls;

/// <summary>Sidebar navigation entry. The selected entry gets the accent gradient and a chevron.</summary>
internal sealed class NavButton : PaintedControl
{
    private bool _selected;

    public NavButton(string text, string glyph)
    {
        Text = text;
        Glyph = glyph;
        Font = Theme.Nav;
        Height = Ui.S(56);
        Cursor = Cursors.Hand;
    }

    public string Glyph { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int inset = Ui.S(4);
        var r = new RectangleF(0, inset, Width - 1, Height - inset * 2 - 1);
        float radius = Ui.S(6f);

        if (_selected)
        {
            using var brush = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), Theme.Accent,
                Theme.Mix(Theme.Accent, Theme.Sidebar, 0.45f), LinearGradientMode.Horizontal);
            Gfx.FillRounded(g, brush, r, radius);
            Gfx.DrawRounded(g, Color.FromArgb(150, Theme.AccentBright), r, radius);
        }
        else if (Hovered)
        {
            Gfx.FillRounded(g, Theme.Hover, r, radius);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(110, Theme.Border));
            g.DrawLine(pen, Ui.S(12), Height - 1, Width - Ui.S(12), Height - 1);
        }

        var glyphColor = _selected ? Color.White : Theme.Mix(Theme.TextDim, Theme.AccentBright, 0.5f);
        Gfx.Glyph(g, Glyph, 15f, glyphColor, new Rectangle(Ui.S(14), 0, Ui.S(32), Height));
        Gfx.Text(g, Text, Font, new Rectangle(Ui.S(60), 0, Width - Ui.S(96), Height), _selected ? Color.White : Theme.Text);

        if (_selected)
            Gfx.Glyph(g, Glyphs.ChevronRight, 10f, Color.White, new Rectangle(Width - Ui.S(36), 0, Ui.S(24), Height));
    }
}

/// <summary>Section title with an icon and a fading accent underline.</summary>
internal sealed class SectionHeader : PaintedControl
{
    public SectionHeader(string title, string glyph)
    {
        Text = title;
        Glyph = glyph;
        Font = Theme.MonoBold;
        Height = Ui.S(50);
    }

    public string Glyph { get; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);

        int textBottom = Height - Ui.S(4);
        Gfx.Glyph(g, Glyph, 16f, Theme.AccentBright, new Rectangle(Ui.S(6), 0, Ui.S(30), textBottom));
        using (var divider = new Pen(Theme.Border))
            g.DrawLine(divider, Ui.S(46), Ui.S(14), Ui.S(46), textBottom - Ui.S(12));
        Gfx.Text(g, Text, Font, new Rectangle(Ui.S(58), 0, Width - Ui.S(60), textBottom), Theme.AccentBright);

        if (Width <= 2) return;
        var line = new Rectangle(0, Height - Ui.S(2), Width, Ui.S(2));
        using var brush = new LinearGradientBrush(new Rectangle(line.X - 1, line.Y, line.Width + 2, line.Height),
            Theme.Accent, Color.FromArgb(25, Theme.Accent), LinearGradientMode.Horizontal);
        g.FillRectangle(brush, line);
    }
}

/// <summary>A labelled settings row: text on the left, an editor control on the right.</summary>
internal sealed class SettingRow : Panel
{
    public SettingRow(string label, Control? editor)
    {
        Text = label;
        Editor = editor;
        Height = Ui.S(46);
        Font = Theme.Mono;
        DoubleBuffered = true;
        ResizeRedraw = true;
        if (editor is not null) Controls.Add(editor);
    }

    public Control? Editor { get; }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        if (Editor is not null)
            Editor.Location = new Point(Width - Editor.Width - Ui.S(16), (Height - Editor.Height) / 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        int right = Editor is null ? Width : Editor.Left - Ui.S(12);
        Gfx.Text(e.Graphics, Text, Font, new Rectangle(Ui.S(16), 0, right - Ui.S(16), Height), Theme.Text);
    }
}

/// <summary>Rounded pill showing a short value, such as a hotkey.</summary>
internal sealed class TagLabel : PaintedControl
{
    private Color _color = Theme.TextDim;

    public TagLabel(string text)
    {
        Font = Theme.MonoSmall;
        Height = Ui.S(28);
        SetText(text, Theme.TextDim);
    }

    public void SetText(string text, Color color)
    {
        if (Text == text && _color == color) return;
        Text = text;
        _color = color;
        Width = TextRenderer.MeasureText(text, Font).Width + Ui.S(24);
        Invalidate();
        Parent?.PerformLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        Gfx.FillRounded(g, Theme.Mix(ParentBack, _color, 0.12f), r, Ui.S(6f));
        Gfx.DrawRounded(g, Theme.Mix(ParentBack, _color, 0.5f), r, Ui.S(6f));
        Gfx.Text(g, Text, Font, ClientRectangle, _color, TextFormatFlags.HorizontalCenter);
    }
}

/// <summary>Wrapped paragraph of dim text that sizes its own height to fit.</summary>
internal sealed class Paragraph : PaintedControl
{
    private const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.NoPrefix;

    public Paragraph(string text)
    {
        Text = text;
        Font = Theme.Body;
        Padding = Ui.Pad(16, 8, 16, 8);
        Height = Ui.S(40);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        int available = Math.Max(Ui.S(50), Width - Padding.Horizontal);
        int height = TextRenderer.MeasureText(Text, Font, new Size(available, int.MaxValue), Flags).Height + Padding.Vertical;
        if (Height != height) Height = height;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(ParentBack);
        TextRenderer.DrawText(e.Graphics, Text, Font,
            new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical),
            Theme.TextDim, Flags);
    }
}
