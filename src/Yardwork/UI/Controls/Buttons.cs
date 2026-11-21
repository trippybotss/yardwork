using System.Drawing.Drawing2D;

namespace Yardwork.UI.Controls;

internal enum ButtonStyle
{
    Primary,
    Ghost,
    Chip,
}

/// <summary>Rounded text button. Primary = filled accent, Ghost = outlined, Chip = selectable filter.</summary>
internal sealed class FlatButton : PaintedControl
{
    private bool _selected;

    public FlatButton(string text, ButtonStyle style = ButtonStyle.Ghost, string? glyph = null)
    {
        SetStyle(ControlStyles.Selectable, true);
        Style = style;
        Glyph = glyph;
        Font = Theme.BodySemibold;
        Cursor = Cursors.Hand;
        Height = Ui.S(34);
        Text = text;
        FitWidth();
    }

    public ButtonStyle Style { get; }
    public string? Glyph { get; }

    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
        }
    }

    private void FitWidth()
    {
        int width = TextRenderer.MeasureText(Text, Font).Width + Ui.S(28);
        if (Glyph is not null) width += Ui.S(22);
        Width = width;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        FitWidth();
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        base.OnEnabledChanged(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        float radius = Ui.S(6f);
        Color fill, border, text;
        switch (Style)
        {
            case ButtonStyle.Primary:
                fill = !Enabled ? Theme.Mix(Theme.Panel, Theme.Accent, 0.2f)
                    : Pressed ? Theme.AccentDeep
                    : Hovered ? Theme.Mix(Theme.Accent, Color.White, 0.12f)
                    : Theme.Accent;
                border = Enabled ? Theme.Mix(fill, Color.White, 0.15f) : fill;
                text = Enabled ? Color.White : Theme.TextMuted;
                break;
            case ButtonStyle.Chip:
                fill = _selected ? Theme.Mix(Theme.Panel, Theme.Accent, 0.4f) : Hovered ? Theme.Hover : Theme.Panel;
                border = _selected ? Theme.Accent : Theme.Border;
                text = _selected ? Color.White : Theme.TextDim;
                break;
            default:
                fill = Pressed ? Theme.Mix(Theme.Hover, Theme.Accent, 0.25f) : Hovered && Enabled ? Theme.Hover : Theme.Panel;
                border = Hovered && Enabled ? Theme.Accent : Theme.Border;
                text = Enabled ? Theme.Text : Theme.TextMuted;
                break;
        }

        Gfx.FillRounded(g, fill, r, radius);
        Gfx.DrawRounded(g, border, r, radius);

        if (Glyph is null)
        {
            Gfx.Text(g, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter);
        }
        else
        {
            int textWidth = TextRenderer.MeasureText(Text, Font).Width;
            int x = (Width - (Ui.S(22) + textWidth)) / 2;
            Gfx.Glyph(g, Glyph, 10f, text, new Rectangle(x, 0, Ui.S(18), Height));
            Gfx.Text(g, Text, Font, new Rectangle(x + Ui.S(22), 0, textWidth + 4, Height), text);
        }

        if (Focused && ShowFocusCues)
            Gfx.DrawRounded(g, Theme.AccentBright, RectangleF.Inflate(r, -2, -2), radius - 2);
    }
}

/// <summary>Icon-only button (window chrome, refresh, settings gear). Transparent over its parent.</summary>
internal sealed class GlyphButton : PaintedControl
{
    private string _glyph;

    public GlyphButton(string glyph)
    {
        _glyph = glyph;
        BackColor = Color.Transparent;
        Size = Ui.S(40, 34);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public string Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            Invalidate();
        }
    }

    public float GlyphSize { get; set; } = 10f;
    public bool Danger { get; set; }
    public Color? GlyphColor { get; set; }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (Hovered)
        {
            var fill = Danger ? Theme.Danger : Pressed ? Theme.Mix(Theme.Hover, Theme.Accent, 0.3f) : Theme.Hover;
            Gfx.FillRounded(g, fill, new RectangleF(0, 0, Width - 1, Height - 1), Ui.S(6f));
        }
        var color = Hovered && Danger ? Color.White : Hovered ? Theme.Text : GlyphColor ?? Theme.TextDim;
        Gfx.Glyph(g, _glyph, GlyphSize, color, ClientRectangle);
    }
}

/// <summary>Small checkbox with a label, used in the status bar.</summary>
internal sealed class DarkCheckBox : PaintedControl
{
    private bool _checked;

    public DarkCheckBox(string text)
    {
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Text = text;
        Height = Ui.S(30);
        Width = Ui.S(30) + TextRenderer.MeasureText(text, Font).Width;
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int size = Ui.S(18);
        var box = new RectangleF(1, (Height - size) / 2f, size, size);
        if (_checked)
        {
            Gfx.FillRounded(g, Theme.Accent, box, Ui.S(4f));
            Gfx.Glyph(g, Glyphs.Check, 9f, Color.White, Rectangle.Round(box));
        }
        else
        {
            Gfx.FillRounded(g, Theme.Panel, box, Ui.S(4f));
            Gfx.DrawRounded(g, Hovered ? Theme.Accent : Theme.Border, box, Ui.S(4f));
        }

        Gfx.Text(g, Text, Font, new Rectangle(size + Ui.S(10), 0, Width - size - Ui.S(10), Height),
            Hovered ? Theme.Text : Theme.TextDim);
    }
}
