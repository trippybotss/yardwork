using Yardwork.Native;
using Yardwork.Overlay;
using Yardwork.Processes;
using Yardwork.UI.Controls;
using Yardwork.UI.Pages;

namespace Yardwork.UI;

/// <summary>Borderless main window: header, sidebar navigation, pages and status bar.</summary>
internal sealed class MainForm : Form
{
    private const int ResizeGrip = 6;

    private readonly AppSettings _settings;
    private readonly OverlayHost _overlay;
    private readonly HeaderBar _header;
    private readonly StatusBar _status;
    private readonly ProcessesPage _processesPage;
    private readonly OverlayPage _overlayPage;
    private readonly SettingsPage _settingsPage;
    private readonly List<(NavButton Button, Control Page)> _navigation = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 400 };
    private HotkeyInfo[]? _shownHotkeys;
    private string? _flashMessage;
    private DateTime _flashUntil;
    private bool _errorShown;

    public MainForm(AppSettings settings, OverlayHost overlay)
    {
        _settings = settings;
        _overlay = overlay;

        SuspendLayout();
        AutoScaleMode = AutoScaleMode.None;
        Text = "Yardwork";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = Ui.S(1140, 760);
        MinimumSize = Ui.S(920, 620);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body;
        Padding = Ui.Pad(ResizeGrip);
        DoubleBuffered = true;
        KeyPreview = true;
        Icon = Logo.CreateIcon();
        TopMost = settings.AlwaysOnTop;

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface };
        _processesPage = new ProcessesPage();
        _overlayPage = new OverlayPage(settings) { Visible = false };
        _settingsPage = new SettingsPage(settings) { Visible = false };
        content.Controls.AddRange([_processesPage, _overlayPage, _settingsPage]);

        var sidebar = new Sidebar { Dock = DockStyle.Left };
        AddNavigation(sidebar, "Processes", Glyphs.Apps, _processesPage);
        AddNavigation(sidebar, "Overlay", Glyphs.View, _overlayPage);
        AddNavigation(sidebar, "Settings", Glyphs.Settings, _settingsPage);

        _status = new StatusBar { Dock = DockStyle.Bottom };
        _header = new HeaderBar(this) { Dock = DockStyle.Top };

        // Dock order: the last control added is docked first.
        Controls.Add(content);
        Controls.Add(sidebar);
        Controls.Add(_status);
        Controls.Add(_header);
        ResumeLayout(false);

        Navigate(_processesPage);

        _processesPage.AttachRequested += Attach;
        _processesPage.DetachRequested += Detach;
        _settingsPage.ResetRequested += ResetSettings;
        _settingsPage.AlwaysOnTopToggle.CheckedChanged += (_, _) => SetAlwaysOnTop(_settingsPage.AlwaysOnTopToggle.Checked);
        _status.AlwaysOnTop.Checked = settings.AlwaysOnTop;
        _status.AlwaysOnTop.CheckedChanged += (_, _) => SetAlwaysOnTop(_status.AlwaysOnTop.Checked);
        _status.SettingsButton.Click += (_, _) => Navigate(_settingsPage);

        _overlay.ShowMenuRequested += BringToFrontFromHotkey;
        _overlay.ExitRequested += () =>
        {
            if (!IsDisposed) Close();
        };
        _overlay.OverlayToggled += () =>
        {
            if (!IsDisposed) _overlayPage.SyncFromSettings();
        };
        _overlay.TargetExited += target =>
        {
            if (IsDisposed) return;
            if (_processesPage.AttachedPid == target.Pid) _processesPage.AttachedPid = null;
            Flash($"{target.ImageName} exited, overlay detached");
        };

        Theme.AccentChanged += OnAccentChanged;
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    public void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            return;
        }
        // Borderless windows would cover the taskbar when maximized; clamp to the working area.
        var screen = Screen.FromControl(this);
        MaximizedBounds = new Rectangle(
            screen.WorkingArea.X - screen.Bounds.X,
            screen.WorkingArea.Y - screen.Bounds.Y,
            screen.WorkingArea.Width,
            screen.WorkingArea.Height);
        WindowState = FormWindowState.Maximized;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= Win32.WS_MINIMIZEBOX; // lets the taskbar button minimize/restore
            cp.ClassStyle |= Win32.CS_DROPSHADOW;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = Win32.DWMWCP_ROUND;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        int dark = 1;
        Win32.DwmSetWindowAttribute(Handle, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg != Win32.WM_NCHITTEST || WindowState != FormWindowState.Normal || m.Result != Win32.HTCLIENT) return;

        long lParam = m.LParam.ToInt64();
        var point = PointToClient(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
        int grip = Ui.S(ResizeGrip);
        bool left = point.X < grip, right = point.X >= ClientSize.Width - grip;
        bool top = point.Y < grip, bottom = point.Y >= ClientSize.Height - grip;

        int hit = (left, right, top, bottom) switch
        {
            (true, _, true, _) => Win32.HTTOPLEFT,
            (_, true, true, _) => Win32.HTTOPRIGHT,
            (true, _, _, true) => Win32.HTBOTTOMLEFT,
            (_, true, _, true) => Win32.HTBOTTOMRIGHT,
            (true, _, _, _) => Win32.HTLEFT,
            (_, true, _, _) => Win32.HTRIGHT,
            (_, _, true, _) => Win32.HTTOP,
            (_, _, _, true) => Win32.HTBOTTOM,
            _ => Win32.HTCLIENT,
        };
        m.Result = hit;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        _header?.UpdateMaximizeGlyph();
        Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : Ui.Pad(ResizeGrip);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (WindowState == FormWindowState.Maximized) return;
        using var pen = new Pen(Theme.Mix(Theme.Border, Theme.Accent, 0.45f));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.F))
        {
            Navigate(_processesPage);
            _processesPage.FocusSearch();
            return true;
        }
        if (keyData == Keys.F5)
        {
            _processesPage.RefreshNow();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statusTimer.Stop();
        Theme.AccentChanged -= OnAccentChanged;
        _overlay.Detach();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _statusTimer.Dispose();
        base.Dispose(disposing);
    }

    private void AddNavigation(Sidebar sidebar, string text, string glyph, Control page)
    {
        var button = sidebar.AddButton(text, glyph);
        button.Click += (_, _) => Navigate(page);
        _navigation.Add((button, page));
    }

    private void Navigate(Control page)
    {
        foreach (var (button, candidate) in _navigation)
        {
            button.Selected = candidate == page;
            candidate.Visible = candidate == page;
        }
    }

    private void Attach(ProcessEntry process)
    {
        _overlay.Attach(new OverlayTarget(process.Pid, process.Name, process.ImageName, process.Window));
        _processesPage.AttachedPid = process.Pid;
        if (!_settings.OverlayEnabled)
        {
            _settings.OverlayEnabled = true;
            _overlayPage.SyncFromSettings();
        }
    }

    private void Detach()
    {
        _overlay.Detach();
        _processesPage.AttachedPid = null;
    }

    private void SetAlwaysOnTop(bool value)
    {
        _settings.AlwaysOnTop = value;
        TopMost = value;
        _status.AlwaysOnTop.Checked = value;
        _settingsPage.AlwaysOnTopToggle.Checked = value;
    }

    private void ResetSettings()
    {
        var answer = MessageBox.Show(this, "Reset every setting to its default?", "Yardwork",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;

        _settings.ResetToDefaults();
        Theme.SetAccent(_settings.Accent);
        _overlayPage.SyncFromSettings();
        _settingsPage.SyncFromSettings();
        SetAlwaysOnTop(_settings.AlwaysOnTop);
    }

    private void BringToFrontFromHotkey()
    {
        if (IsDisposed) return;
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        // Toggling TopMost forces the window above the foreground app.
        TopMost = true;
        TopMost = _settings.AlwaysOnTop;
        Activate();
    }

    private void OnAccentChanged()
    {
        _processesPage.ApplyAccent();
        Invalidate(true);
    }

    private void Flash(string message)
    {
        _flashMessage = message;
        _flashUntil = DateTime.UtcNow.AddSeconds(5);
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var state = _overlay.State;
        (string text, Color color) = state switch
        {
            OverlayState.Starting => ("Starting…", Theme.TextDim),
            OverlayState.Idle => ("Ready", Theme.Success),
            OverlayState.Attached => ("Attached", Theme.Success),
            OverlayState.WaitingForWindow => ("Waiting for window", Theme.Warning),
            OverlayState.TargetMinimized => ("Target minimized", Theme.Warning),
            OverlayState.TargetInBackground => ("Target in background", Theme.TextDim),
            OverlayState.Disabled => ("Overlay off", Theme.TextDim),
            _ => ("Overlay error", Theme.Danger),
        };
        if (_flashMessage is not null && DateTime.UtcNow < _flashUntil)
            (text, color) = (_flashMessage, Theme.Warning);

        var target = _overlay.Target;
        string fps = state == OverlayState.Attached ? $"{_overlay.Fps:0}" : "—";
        string targetText = target is null ? "None" : $"{target.ImageName} (PID {target.Pid})";
        _status.SetValues(text, color, fps, targetText);

        var hotkeys = _overlay.Hotkeys;
        if (hotkeys.Count > 0 && !ReferenceEquals(hotkeys, _shownHotkeys))
        {
            _shownHotkeys = hotkeys as HotkeyInfo[];
            _settingsPage.UpdateHotkeys(hotkeys);
        }

        if (state == OverlayState.Error && !_errorShown)
        {
            _errorShown = true;
            MessageBox.Show(this,
                $"The overlay stopped: {_overlay.LastError}\n\nDetails are in {Log.FilePath}",
                "Yardwork", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
