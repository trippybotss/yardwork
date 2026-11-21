using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Yardwork.UI;

/// <summary>The eye logo, drawn in code so it follows the accent color.</summary>
internal static class Logo
{
    public static void DrawEye(Graphics g, RectangleF r, Color accent)
    {
        var bright = Theme.Mix(accent, Color.White, 0.42f);
        var deep = Theme.Mix(accent, Color.Black, 0.45f);
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;

        using var almond = new GraphicsPath();
        almond.AddBezier(r.Left, cy,
            r.Left + r.Width * 0.28f, r.Top - r.Height * 0.12f,
            r.Right - r.Width * 0.28f, r.Top - r.Height * 0.12f,
            r.Right, cy);
        almond.AddBezier(r.Right, cy,
            r.Right - r.Width * 0.28f, r.Bottom + r.Height * 0.12f,
            r.Left + r.Width * 0.28f, r.Bottom + r.Height * 0.12f,
            r.Left, cy);
        almond.CloseFigure();

        using (var glow = new Pen(Color.FromArgb(70, accent), r.Height * 0.16f) { LineJoin = LineJoin.Round })
            g.DrawPath(glow, almond);
        using (var sclera = new SolidBrush(Theme.Mix(Theme.Bg, accent, 0.18f)))
            g.FillPath(sclera, almond);

        var state = g.Save();
        g.SetClip(almond, CombineMode.Intersect);

        float radius = r.Height * 0.46f;
        var iris = new RectangleF(cx - radius, cy - radius, radius * 2, radius * 2);
        using (var irisBrush = new LinearGradientBrush(iris, bright, deep, LinearGradientMode.Vertical))
            g.FillEllipse(irisBrush, iris);
        using (var ring = new Pen(Color.FromArgb(150, bright), Math.Max(1f, r.Height * 0.04f)))
            g.DrawEllipse(ring, RectangleF.Inflate(iris, -radius * 0.28f, -radius * 0.28f));

        float pupilWidth = radius * 0.38f, pupilHeight = radius * 1.35f;
        using (var pupil = new SolidBrush(Color.FromArgb(255, 12, 8, 20)))
            g.FillEllipse(pupil, cx - pupilWidth / 2, cy - pupilHeight / 2, pupilWidth, pupilHeight);
        using (var highlight = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
            g.FillEllipse(highlight, cx - radius * 0.58f, cy - radius * 0.62f, radius * 0.32f, radius * 0.32f);

        g.Restore(state);

        using var outline = new Pen(bright, Math.Max(1.5f, r.Height * 0.06f)) { LineJoin = LineJoin.Round };
        g.DrawPath(outline, almond);
    }

    /// <summary>Window/taskbar icon. The HICON lives for the whole process, so it is never destroyed.</summary>
    public static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            DrawEye(g, new RectangleF(3, 14, 58, 36), Theme.Accent);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
