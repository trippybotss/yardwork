using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Yardwork.UI;

/// <summary>Pixel scaling for the system DPI. Every size in the UI goes through <see cref="S(int)"/>.</summary>
internal static class Ui
{
    public static readonly float Scale = ReadScale();

    public static int S(int value) => (int)Math.Round(value * Scale);
    public static float S(float value) => value * Scale;
    public static Size S(int width, int height) => new(S(width), S(height));
    public static Padding Pad(int all) => new(S(all));
    public static Padding Pad(int left, int top, int right, int bottom) => new(S(left), S(top), S(right), S(bottom));

    private static float ReadScale()
    {
        try
        {
            using var graphics = Graphics.FromHwnd(IntPtr.Zero);
            return graphics.DpiX / 96f;
        }
        catch
        {
            return 1f;
        }
    }
}

/// <summary>Dark purple palette and fonts. The accent color is user-selectable.</summary>
internal static class Theme
{
    private static readonly HashSet<string> InstalledFonts = LoadInstalledFonts();
    private static readonly Dictionary<float, Font> IconFonts = new();

    public static readonly Color Bg = Hex(0x0B0912);
    public static readonly Color Sidebar = Hex(0x0E0B15);
    public static readonly Color Surface = Hex(0x110E18);
    public static readonly Color Panel = Hex(0x17131F);
    public static readonly Color Row = Hex(0x15111D);
    public static readonly Color RowAlt = Hex(0x191422);
    public static readonly Color Hover = Hex(0x231C31);
    public static readonly Color Border = Hex(0x2A2240);
    public static readonly Color Text = Hex(0xECE8F7);
    public static readonly Color TextDim = Hex(0xA39BBE);
    public static readonly Color TextMuted = Hex(0x6F6789);
    public static readonly Color Success = Hex(0x5EE08A);
    public static readonly Color Warning = Hex(0xF5B94A);
    public static readonly Color Danger = Hex(0xE5484D);

    public static readonly Color[] AccentPresets =
    [
        Hex(0x7C3AED), Hex(0x9333EA), Hex(0xC026D3), Hex(0xE11D48),
        Hex(0xF59E0B), Hex(0x10B981), Hex(0x06B6D4), Hex(0x2563EB),
    ];

    public static Color Accent { get; private set; } = AccentPresets[0];
    public static Color AccentBright => Mix(Accent, Color.White, 0.38f);
    public static Color AccentDeep => Mix(Accent, Color.Black, 0.35f);
    public static event Action? AccentChanged;

    public static readonly string MonoFamily = FirstInstalled("Cascadia Mono", "Consolas");
    public static readonly string IconFamily = FirstInstalled("Segoe Fluent Icons", "Segoe MDL2 Assets");

    public static readonly Font Body = new("Segoe UI", 9.75f);
    public static readonly Font BodySemibold = new("Segoe UI Semibold", 9.75f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font Mono = new(MonoFamily, 10.5f);
    public static readonly Font MonoSmall = new(MonoFamily, 9f);
    public static readonly Font MonoBold = new(MonoFamily, 11.5f, FontStyle.Bold);
    public static readonly Font Nav = new(MonoFamily, 11.5f);
    public static readonly Font Title = new(FirstInstalled("Segoe UI Black", "Segoe UI"), 26f, FontStyle.Italic);
    public static readonly Font Subtitle = new("Segoe UI Semibold", 8.5f);

    public static void SetAccent(Color color)
    {
        color = Color.FromArgb(255, color.R, color.G, color.B);
        if (color.ToArgb() == Accent.ToArgb()) return;
        Accent = color;
        AccentChanged?.Invoke();
    }

    public static Font Icon(float size)
    {
        if (!IconFonts.TryGetValue(size, out var font))
            IconFonts[size] = font = new Font(IconFamily, size);
        return font;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    public static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static string FirstInstalled(params string[] families)
    {
        foreach (var family in families)
            if (InstalledFonts.Contains(family)) return family;
        return families[^1];
    }

    private static HashSet<string> LoadInstalledFonts()
    {
        try
        {
            using var collection = new InstalledFontCollection();
            return collection.Families.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

/// <summary>Segoe Fluent Icons / Segoe MDL2 Assets code points.</summary>
internal static class Glyphs
{
    public const string Apps = "";
    public const string View = "";
    public const string Settings = "";
    public const string Info = "";
    public const string Palette = "";
    public const string Keyboard = "";
    public const string Search = "";
    public const string Refresh = "";
    public const string ChevronRight = "";
    public const string Minimize = "";
    public const string Maximize = "";
    public const string Restore = "";
    public const string Close = "";
    public const string Link = "";
    public const string Add = "";
    public const string Check = "";
    public const string Star = "";
}

internal static class Gfx
{
    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        using var brush = new SolidBrush(color);
        FillRounded(g, brush, r, radius);
    }

    public static void FillRounded(Graphics g, Brush brush, RectangleF r, float radius)
    {
        using var path = Rounded(r, radius);
        g.FillPath(brush, path);
    }

    public static void DrawRounded(Graphics g, Color color, RectangleF r, float radius, float width = 1f)
    {
        using var pen = new Pen(color, width);
        using var path = Rounded(r, radius);
        g.DrawPath(pen, path);
    }

    public static void Glyph(Graphics g, string glyph, float size, Color color, Rectangle bounds) =>
        TextRenderer.DrawText(g, glyph, Theme.Icon(size), bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

    public static void Text(Graphics g, string text, Font font, Rectangle bounds, Color color,
        TextFormatFlags align = TextFormatFlags.Left) =>
        TextRenderer.DrawText(g, text, font, bounds, color,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
            TextFormatFlags.EndEllipsis);
}
