using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Yardwork.UI;

/// <summary>Small program icons for the process list, cached by executable path.</summary>
internal static class IconCache
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();
    private static readonly int Size = Ui.S(16);
    private static readonly Bitmap Fallback = ToBitmap(SystemIcons.Application);

    /// <summary>Loads icons for new paths; safe to call from a worker thread.</summary>
    public static void Warm(IEnumerable<string?> paths)
    {
        foreach (var path in paths)
            if (path is not null) Get(path);
    }

    public static Bitmap Get(string? path)
    {
        if (path is null) return Fallback;

        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }

        Bitmap? loaded = Load(path);

        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var existing))
            {
                loaded?.Dispose();
                return existing;
            }
            var bitmap = loaded ?? Fallback;
            Cache[path] = bitmap;
            return bitmap;
        }
    }

    private static Bitmap? Load(string path)
    {
        try
        {
            using Icon? icon = Icon.ExtractIcon(path, 0, Size) ?? Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : ToBitmap(icon);
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap ToBitmap(Icon icon)
    {
        var bitmap = new Bitmap(Size, Size, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bitmap);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawIcon(icon, new Rectangle(0, 0, Size, Size));
        return bitmap;
    }
}
