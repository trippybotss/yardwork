namespace Yardwork;

/// <summary>Tiny append-only log in %AppData%\Yardwork, handy when something fails on another machine.</summary>
internal static class Log
{
    private static readonly object Gate = new();

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yardwork");

    public static string FilePath => Path.Combine(Directory, "yardwork.log");

    public static void Error(string where, Exception ex) => Write($"[{where}] {ex}");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
