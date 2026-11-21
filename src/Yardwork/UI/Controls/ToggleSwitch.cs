using System.Drawing.Drawing2D;

namespace Yardwork.UI.Controls;

/// <summary>Pill-shaped on/off switch with a short slide animation.</summary>
internal sealed class ToggleSwitch : PaintedControl
{
    private readonly System.Windows.Forms.Timer _animation = new() { Interval = 15 };
    private bool _checked;
    private float _position;

    public ToggleSwitch()
    {
        SetStyle(ControlStyles.Selectable, true);
        Size = Ui.S(48, 26);
        Cursor = Cursors.Hand;
        TabStop = true;
        _animation.Tick += (_, _) =>
        {
            float target = _checked ? 1f : 0f;
            _position += Math.Sign(target - _position) * 0.2f;
            if (Math.Abs(target - _position) < 0.2f)
            {
                _position = target;
                _animation.Stop();
            }
            Invalidate();
        };
    }

    public event EventHandler? CheckedChanged;

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            if (IsHandleCreated && Visible) _animation.Start();
            else _position = value ? 1f : 0f;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(ParentBack);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var track = new RectangleF(1, 1, Width - 3, Height - 3);
        float radius = track.Height / 2f;
        var off = Theme.Mix(Theme.Panel, Theme.Border, 0.6f);
        Gfx.FillRounded(g, Theme.Mix(off, Theme.Accent, _position), track, radius);
        Gfx.DrawRounded(g, Theme.Mix(Hovered ? Theme.TextMuted : Theme.Border, Theme.AccentBright, _position), track, radius);

        float knob = track.Height - Ui.S(6f);
        float x = track.X + Ui.S(3f) + (track.Width - Ui.S(6f) - knob) * _position;
        using var brush = new SolidBrush(_checked ? Color.White : Theme.Hex(0xD9D3EA));
        g.FillEllipse(brush, x, track.Y + Ui.S(3f), knob, knob);

        if (Focused && ShowFocusCues)
            Gfx.DrawRounded(g, Color.FromArgb(140, Theme.AccentBright), RectangleF.Inflate(track, -1, -1), radius - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
