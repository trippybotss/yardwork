using System.Text;
using Yardwork.Native;

namespace Yardwork.Processes;

internal readonly record struct WindowInfo(IntPtr Handle, string Title);

/// <summary>
/// Finds the "main" top-level window of a process the same way Task Manager decides what counts
/// as an app: visible, not owned by another window, not a tool window, not cloaked, has a title.
/// When a process has several, the biggest one wins.
/// </summary>
internal static class WindowFinder
{
    public static Dictionary<int, WindowInfo> MainWindowsByProcess()
    {
        var best = new Dictionary<int, (IntPtr Hwnd, long Score)>();
        int self = Environment.ProcessId;

        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if ((int)pid == self || !IsCandidate(hwnd)) return true;

            long score = Score(hwnd);
            if (!best.TryGetValue((int)pid, out var current) || score > current.Score)
                best[(int)pid] = (hwnd, score);
            return true;
        }, IntPtr.Zero);

        var result = new Dictionary<int, WindowInfo>(best.Count);
        foreach (var (pid, entry) in best)
            result[pid] = new WindowInfo(entry.Hwnd, GetTitle(entry.Hwnd));
        return result;
    }

    public static WindowInfo? FindMainWindow(int pid)
    {
        IntPtr found = IntPtr.Zero;
        long bestScore = -1;

        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out uint owner);
            if ((int)owner != pid || !IsCandidate(hwnd)) return true;

            long score = Score(hwnd);
            if (score > bestScore)
            {
                bestScore = score;
                found = hwnd;
            }
            return true;
        }, IntPtr.Zero);

        return found == IntPtr.Zero ? null : new WindowInfo(found, GetTitle(found));
    }

    public static string GetTitle(IntPtr hwnd)
    {
        int length = Win32.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var text = new StringBuilder(length + 1);
        Win32.GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    private static bool IsCandidate(IntPtr hwnd)
    {
        if (!Win32.IsWindowVisible(hwnd)) return false;
        if (Win32.GetWindow(hwnd, Win32.GW_OWNER) != IntPtr.Zero) return false;
        if ((Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0) return false;
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        return Win32.GetWindowTextLength(hwnd) > 0;
    }

    private static long Score(IntPtr hwnd)
    {
        if (Win32.IsIconic(hwnd)) return 1;
        return Win32.GetWindowRect(hwnd, out var rect) ? Math.Max(1L, (long)rect.Width * rect.Height) : 1;
    }
}
