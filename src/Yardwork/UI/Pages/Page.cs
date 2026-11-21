using Yardwork.Native;
using Yardwork.UI.Controls;

namespace Yardwork.UI.Pages;

/// <summary>
/// Scrollable page of stacked sections and rows. Rows live in an inner panel so the page's
/// AutoScroll sees real bounds (docked children never produce scrollbars on their own).
/// </summary>
internal class Page : Panel
{
    private readonly Panel _stack;
    private readonly List<(ToggleSwitch Toggle, Func<bool> Get)> _bindings = new();
    private int _row;

    public Page()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Surface;
        AutoScroll = true;
        DoubleBuffered = true;
        AutoScrollMargin = new Size(0, Ui.S(18));
        _stack = new Panel { BackColor = Theme.Surface };
        Controls.Add(_stack);
    }

    /// <summary>Re-reads every bound toggle from settings (after a hotkey or a reset).</summary>
    public void SyncFromSettings()
    {
        foreach (var (toggle, get) in _bindings) toggle.Checked = get();
    }

    protected void AddTop(Control control)
    {
        control.Dock = DockStyle.Top;
        _stack.Controls.Add(control);
        control.BringToFront(); // docks it below the previously added controls
        control.SizeChanged += (_, _) => PerformLayout();
        PerformLayout();
    }

    protected void AddSpacer(int height) => AddTop(new Panel { Height = Ui.S(height), BackColor = Theme.Surface });

    protected void AddSection(string title, string glyph)
    {
        if (_stack.Controls.Count > 0) AddSpacer(22);
        AddTop(new SectionHeader(title, glyph));
        AddSpacer(6);
        _row = 0;
    }

    protected SettingRow AddRow(string label, Control? editor)
    {
        var row = new SettingRow(label, editor) { BackColor = _row++ % 2 == 0 ? Theme.Row : Theme.RowAlt };
        AddTop(row);
        return row;
    }

    protected ToggleSwitch AddToggle(string label, Func<bool> get, Action<bool> set)
    {
        var toggle = new ToggleSwitch { Checked = get() };
        toggle.CheckedChanged += (_, _) => set(toggle.Checked);
        _bindings.Add((toggle, get));
        AddRow(label, toggle);
        return toggle;
    }

    protected void AddParagraph(string text) => AddTop(new Paragraph(text));

    protected override void OnLayout(LayoutEventArgs levent)
    {
        if (_stack is not null)
        {
            int width = Math.Max(Ui.S(240), ClientSize.Width - Ui.S(48));
            int height = 0;
            foreach (Control child in _stack.Controls) height += child.Height;
            _stack.SetBounds(Ui.S(24) + AutoScrollPosition.X, Ui.S(14) + AutoScrollPosition.Y, width, height);
        }
        base.OnLayout(levent);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Win32.SetWindowTheme(Handle, "DarkMode_Explorer", null);
    }
}
