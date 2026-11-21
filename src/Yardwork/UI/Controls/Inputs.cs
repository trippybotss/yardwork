using System.Drawing.Drawing2D;
using Yardwork.Native;

namespace Yardwork.UI.Controls;

/// <summary>Row of accent swatches plus a "+" swatch that opens the full color picker.</summary>
internal sealed class AccentPicker : PaintedControl
{
    private int _hover = -1;

    public AccentPicker()
    {
        Height = Ui.S(30);
        Width = Height * (Theme.AccentPresets.Length + 1);
    }

    public event Action<Color>? ColorPicked;

    private int SwatchCount => Theme.AccentPresets.Length + 1;

    private RectangleF SwatchBounds(int index)
    {
        float size = Height - Ui.S(8f);
        return new RectangleF(index * Height + (Height - size) / 2f, (Height - size) / 2f, size, size);
    }

    private int HitTest(Point point)
    {
        int index = point.X / Math.Max(1, Height);
        return index >= 0 && index < SwatchCount ? index : -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hover = HitTest(e.Location);
        if (hover == _hover) return;
        _hover = hover;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int index = HitTest(e.Location);
        if (index < 0) return;

        if (index < Theme.AccentPresets.Length)
        {
            ColorPicked?.Invoke(Theme.AccentPresets[index]);
            return;
        }

        using var dialog = new ColorDialog { Color = Theme.Accent, FullOpen = true, AnyColor = true };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
            ColorPicked?.Invoke(dialog.Color);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool presetSelected = false;
        for (int i = 0; i < Theme.AccentPresets.Length; i++)
        {
            var color = Theme.AccentPresets[i];
            var r = SwatchBounds(i);
            using (var brush = new SolidBrush(color)) g.FillEllipse(brush, r);

            bool selected = color.ToArgb() == Theme.Accent.ToArgb();
            presetSelected |= selected;
            if (selected || i == _hover)
            {
                using var ring = new Pen(selected ? Color.White : Theme.TextDim, Ui.S(2f));
                g.DrawEllipse(ring, RectangleF.Inflate(r, Ui.S(2.5f), Ui.S(2.5f)));
            }
        }

        // Custom color swatch: shows the current accent when it isn't one of the presets.
        var custom = SwatchBounds(Theme.AccentPresets.Length);
        if (!presetSelected)
        {
            using var brush = new SolidBrush(Theme.Accent);
            g.FillEllipse(brush, custom);
            using var ring = new Pen(Color.White, Ui.S(2f));
            g.DrawEllipse(ring, RectangleF.Inflate(custom, Ui.S(2.5f), Ui.S(2.5f)));
        }
        else
        {
            using var dashed = new Pen(_hover == Theme.AccentPresets.Length ? Theme.Text : Theme.TextMuted, Ui.S(1.5f))
            {
                DashStyle = DashStyle.Dash,
            };
            g.DrawEllipse(dashed, custom);
        }
        Gfx.Glyph(g, Glyphs.Add, 8f, presetSelected ? Theme.TextDim : Color.White, Rectangle.Round(custom));
    }
}

/// <summary>Rounded search field with an icon and placeholder text.</summary>
internal sealed class SearchBox : PaintedControl
{
    private readonly TextBox _box;

    public SearchBox()
    {
        Size = Ui.S(300, 34);
        Cursor = Cursors.IBeam;
        _box = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            PlaceholderText = "Search name, PID or window title",
        };
        _box.TextChanged += (_, _) => QueryChanged?.Invoke(this, EventArgs.Empty);
        _box.GotFocus += (_, _) => Invalidate();
        _box.LostFocus += (_, _) => Invalidate();
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Escape) return;
            _box.Clear();
            e.Handled = e.SuppressKeyPress = true;
        };
        Controls.Add(_box);
    }

    public event EventHandler? QueryChanged;

    public string Query => _box.Text;

    public void FocusInput()
    {
        _box.Focus();
        _box.SelectAll();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        int left = Ui.S(34);
        _box.SetBounds(left, (Height - _box.Height) / 2, Math.Max(10, Width - left - Ui.S(10)), _box.Height);
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        _box.Focus();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(0.5f, 0.5f, Width - 2f, Height - 2f);
        Gfx.FillRounded(g, Theme.Panel, r, Ui.S(6f));
        Gfx.DrawRounded(g, _box.Focused ? Theme.Accent : Hovered ? Theme.TextMuted : Theme.Border, r, Ui.S(6f));
        Gfx.Glyph(g, Glyphs.Search, 10f, Theme.TextMuted, new Rectangle(Ui.S(6), 0, Ui.S(26), Height));
    }
}

/// <summary>DataGridView restyled for the dark theme.</summary>
internal sealed class DarkGrid : DataGridView
{
    public DarkGrid()
    {
        DoubleBuffered = true;
        BorderStyle = BorderStyle.None;
        BackgroundColor = Theme.Surface;
        GridColor = Theme.Mix(Theme.Surface, Theme.Border, 0.6f);
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        EnableHeadersVisualStyles = false;
        RowHeadersVisible = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        ReadOnly = true;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        StandardTab = true;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        ColumnHeadersHeight = Ui.S(36);
        RowTemplate.Height = Ui.S(30);

        DefaultCellStyle.BackColor = Theme.Surface;
        DefaultCellStyle.ForeColor = Theme.Text;
        DefaultCellStyle.Font = Theme.Body;
        DefaultCellStyle.Padding = new Padding(Ui.S(6), 0, Ui.S(6), 0);
        DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        AlternatingRowsDefaultCellStyle.BackColor = Theme.Row;

        ColumnHeadersDefaultCellStyle.BackColor = Theme.Panel;
        ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextDim;
        ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.Panel;
        ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.TextDim;
        ColumnHeadersDefaultCellStyle.Font = Theme.BodySemibold;
        ColumnHeadersDefaultCellStyle.Padding = new Padding(Ui.S(6), 0, Ui.S(6), 0);
        ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;

        ApplyAccent();
    }

    public event EventHandler? EnterPressed;

    public void ApplyAccent()
    {
        var selection = Theme.Mix(Theme.Surface, Theme.Accent, 0.45f);
        DefaultCellStyle.SelectionBackColor = selection;
        DefaultCellStyle.SelectionForeColor = Color.White;
        AlternatingRowsDefaultCellStyle.SelectionBackColor = selection;
        AlternatingRowsDefaultCellStyle.SelectionForeColor = Color.White;
        Invalidate();
    }

    protected override bool ProcessDataGridViewKey(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            EnterPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessDataGridViewKey(e);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            EnterPressed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessDialogKey(keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Dark scrollbars on Windows 10 1809+; ignored elsewhere.
        Win32.SetWindowTheme(Handle, "DarkMode_Explorer", null);
        foreach (Control child in Controls)
            Win32.SetWindowTheme(child.Handle, "DarkMode_Explorer", null);
    }
}
