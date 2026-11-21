using System.Numerics;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;
using DWriteFontStyle = Vortice.DirectWrite.FontStyle;
using RectangleF = System.Drawing.RectangleF;

namespace Yardwork.Overlay;

/// <summary>Everything the scene needs to draw one frame.</summary>
internal readonly record struct OverlayFrame(
    OverlayTarget Target,
    int Width,
    int Height,
    float Scale,
    bool NoWindowMode,
    float Fps,
    SystemStats Stats,
    double SecondsSinceAttach,
    AppSettings Settings);

/// <summary>
/// What gets drawn on the overlay. This is the place to add your own widgets: draw with
/// <see cref="ID2D1DeviceContext"/> (shapes, text, bitmaps) inside <see cref="Draw"/>.
/// Coordinates are in DIPs: (0,0) is the top-left of the target window.
/// </summary>
internal sealed class OverlayScene
{
    private ID2D1SolidColorBrush? _brush;
    private IDWriteTextFormat? _label;
    private IDWriteTextFormat? _title;
    private IDWriteTextFormat? _body;
    private IDWriteTextFormat? _value;
    private IDWriteTextFormat? _valueRight;
    private IDWriteTextFormat? _clock;
    private IDWriteTextFormat? _hint;

    public void CreateResources(Renderer renderer)
    {
        _brush = renderer.D2D.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        _label = CreateFormat(renderer, "Segoe UI", 10.5f, FontWeight.Bold);
        _title = CreateFormat(renderer, "Segoe UI", 16f, FontWeight.SemiBold);
        _body = CreateFormat(renderer, "Segoe UI", 12f, FontWeight.Normal);
        _value = CreateFormat(renderer, "Consolas", 12.5f, FontWeight.Normal);
        _valueRight = CreateFormat(renderer, "Consolas", 12.5f, FontWeight.Normal, Vortice.DirectWrite.TextAlignment.Trailing);
        _clock = CreateFormat(renderer, "Segoe UI", 22f, FontWeight.SemiBold, Vortice.DirectWrite.TextAlignment.Center);
        _hint = CreateFormat(renderer, "Segoe UI", 12.5f, FontWeight.Normal, Vortice.DirectWrite.TextAlignment.Center);
    }

    public void ReleaseResources()
    {
        _brush?.Dispose();
        _label?.Dispose();
        _title?.Dispose();
        _body?.Dispose();
        _value?.Dispose();
        _valueRight?.Dispose();
        _clock?.Dispose();
        _hint?.Dispose();
        _brush = null;
        _label = _title = _body = _value = _valueRight = _clock = _hint = null;
    }

    public void Draw(ID2D1DeviceContext context, in OverlayFrame frame)
    {
        if (_brush is null) return;

        float scale = frame.Scale;
        context.Transform = Matrix3x2.CreateScale(scale);
        float width = frame.Width / scale;
        float height = frame.Height / scale;
        var settings = frame.Settings;
        int accent = settings.AccentArgb;

        if (settings.ShowBorder)
        {
            _brush.Color = Rgb(accent, 0.95f);
            context.DrawRectangle(new Rect(1.5f, 1.5f, width - 3f, height - 3f), _brush, 3f);
        }

        if (settings.ShowInfoPanel)
            DrawInfoPanel(context, frame, 16, 16, accent);

        if (settings.ShowPerfHud && width > 560)
            DrawPerformance(context, frame, width - 16 - 250, 16, accent);

        if (settings.ShowClock)
            DrawClock(context, width - 16 - 140, height - 16 - 50, accent);

        if (frame.SecondsSinceAttach < 6)
        {
            float fade = (float)Math.Clamp(6 - frame.SecondsSinceAttach, 0, 1);
            DrawHint(context, width, height, accent, fade);
        }

        context.Transform = Matrix3x2.Identity;
    }

    private void DrawInfoPanel(ID2D1DeviceContext context, in OverlayFrame frame, float x, float y, int accent)
    {
        const float width = 290;
        float height = frame.NoWindowMode ? 104 : 96;
        DrawPanel(context, x, y, width, height, accent, 1f);

        _brush!.Color = Bright(accent, 1f);
        DrawText(context, "YARDWORK  ·  OVERLAY", _label!, x + 18, y + 10, width - 30, 16);

        _brush.Color = new Color4(1f, 1f, 1f, 1f);
        DrawText(context, frame.Target.Name, _title!, x + 18, y + 28, width - 30, 24);

        _brush.Color = new Color4(0.78f, 0.75f, 0.86f, 1f);
        DrawText(context, $"{frame.Target.ImageName}  ·  PID {frame.Target.Pid}", _body!, x + 18, y + 54, width - 30, 18);

        string detail = frame.NoWindowMode
            ? "No window: covering the primary monitor"
            : $"{frame.Width} × {frame.Height} px";
        DrawText(context, detail, _body!, x + 18, y + 72, width - 30, 18);
    }

    private void DrawPerformance(ID2D1DeviceContext context, in OverlayFrame frame, float x, float y, int accent)
    {
        const float width = 250, height = 136;
        DrawPanel(context, x, y, width, height, accent, 1f);
        var stats = frame.Stats;

        _brush!.Color = Bright(accent, 1f);
        DrawText(context, "PERFORMANCE", _label!, x + 18, y + 10, width - 30, 16);

        float rowY = y + 34;
        Row(context, x, rowY, "Overlay", $"{frame.Fps:0} fps", -1, accent);

        rowY += 24;
        Row(context, x, rowY, "CPU", $"{stats.CpuPercent:0}%", stats.CpuPercent / 100f, accent);

        rowY += 24;
        double usedGb = stats.MemoryUsed / 1073741824.0, totalGb = stats.MemoryTotal / 1073741824.0;
        float memoryFraction = stats.MemoryTotal > 0 ? (float)(stats.MemoryUsed / (double)stats.MemoryTotal) : 0;
        Row(context, x, rowY, "RAM", $"{usedGb:0.0}/{totalGb:0} GB", memoryFraction, accent);

        rowY += 24;
        string target = stats.TargetCpuPercent < 0
            ? "n/a"
            : $"{stats.TargetCpuPercent:0}% · {stats.TargetWorkingSet / 1048576.0:0} MB";
        Row(context, x, rowY, "Target", target, -1, accent);
    }

    private void Row(ID2D1DeviceContext context, float panelX, float y, string label, string value, float fraction, int accent)
    {
        _brush!.Color = new Color4(0.70f, 0.67f, 0.80f, 1f);
        DrawText(context, label, _body!, panelX + 18, y, 70, 18);

        if (fraction >= 0)
        {
            const float barWidth = 64;
            float barX = panelX + 82, barY = y + 7;
            _brush.Color = new Color4(1f, 1f, 1f, 0.10f);
            context.FillRoundedRectangle(new RoundedRectangle(new RectangleF(barX, barY, barWidth, 5), 2.5f, 2.5f), _brush);
            _brush.Color = Rgb(accent, 1f);
            float filled = Math.Clamp(fraction, 0, 1) * barWidth;
            if (filled > 0.5f)
                context.FillRoundedRectangle(new RoundedRectangle(new RectangleF(barX, barY, filled, 5), 2.5f, 2.5f), _brush);
        }

        _brush.Color = new Color4(1f, 1f, 1f, 1f);
        DrawText(context, value, _valueRight!, panelX + 82, y - 1, 250 - 82 - 18, 18);
    }

    private void DrawClock(ID2D1DeviceContext context, float x, float y, int accent)
    {
        DrawPanel(context, x, y, 140, 50, accent, 1f);
        _brush!.Color = new Color4(1f, 1f, 1f, 1f);
        DrawText(context, DateTime.Now.ToString("HH:mm:ss"), _clock!, x + 6, y + 9, 128, 32);
    }

    private void DrawHint(ID2D1DeviceContext context, float width, float height, int accent, float alpha)
    {
        const float hintWidth = 520, hintHeight = 36;
        float x = (width - hintWidth) / 2, y = height - hintHeight - 24;
        if (x < 8) return;
        DrawPanel(context, x, y, hintWidth, hintHeight, accent, alpha);
        _brush!.Color = new Color4(1f, 1f, 1f, alpha);
        DrawText(context, "Ctrl+Shift+F8 hide overlay  ·  Ctrl+Shift+F9 open Yardwork  ·  Ctrl+Shift+F10 exit",
            _hint!, x + 10, y + 8, hintWidth - 20, 20);
    }

    private void DrawPanel(ID2D1DeviceContext context, float x, float y, float width, float height, int accent, float alpha)
    {
        var shape = new RoundedRectangle(new RectangleF(x, y, width, height), 10, 10);
        _brush!.Color = new Color4(0.055f, 0.043f, 0.086f, 0.82f * alpha);
        context.FillRoundedRectangle(shape, _brush);
        _brush.Color = Rgb(accent, 0.55f * alpha);
        context.DrawRoundedRectangle(shape, _brush, 1f);
        _brush.Color = Rgb(accent, alpha);
        context.FillRoundedRectangle(new RoundedRectangle(new RectangleF(x, y + 12, 3, height - 24), 1.5f, 1.5f), _brush);
    }

    private void DrawText(ID2D1DeviceContext context, string text, IDWriteTextFormat format, float x, float y, float width, float height) =>
        context.DrawText(text, format, new Rect(x, y, width, height), _brush!, DrawTextOptions.Clip);

    private static Color4 Rgb(int argb, float alpha) =>
        new(((argb >> 16) & 0xFF) / 255f, ((argb >> 8) & 0xFF) / 255f, (argb & 0xFF) / 255f, alpha);

    private static Color4 Bright(int argb, float alpha)
    {
        var c = Rgb(argb, alpha);
        return new Color4(c.R + (1 - c.R) * 0.4f, c.G + (1 - c.G) * 0.4f, c.B + (1 - c.B) * 0.4f, alpha);
    }

    private static IDWriteTextFormat CreateFormat(Renderer renderer, string family, float size, FontWeight weight,
        Vortice.DirectWrite.TextAlignment alignment = Vortice.DirectWrite.TextAlignment.Leading)
    {
        var format = renderer.DirectWrite.CreateTextFormat(family, weight, DWriteFontStyle.Normal, FontStretch.Normal, size);
        format.WordWrapping = WordWrapping.NoWrap;
        format.TextAlignment = alignment;
        return format;
    }
}
