using System.Diagnostics;
using Yardwork.Native;

namespace Yardwork.Processes;

internal sealed class ProcessEntry
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public required string ImageName { get; init; }
    public string? Path { get; init; }
    public IntPtr Window { get; init; }
    public string WindowTitle { get; init; } = "";
    public long Memory { get; init; }
    public double? Cpu { get; init; }

    /// <summary>True when the process owns a visible top-level window (Task Manager's "Apps" group).</summary>
    public bool IsApp => Window != IntPtr.Zero;
}

/// <summary>
/// Takes snapshots of every running process: name, description, PID, CPU, memory and main window.
/// CPU usage is the change in process time between two scans, so the first scan shows none.
/// Call <see cref="Scan"/> from one thread at a time.
/// </summary>
internal sealed class ProcessScanner
{
    private sealed record Identity(string? Path, string Name, string ImageName);

    private Dictionary<(int Pid, long Created), Identity> _identities = new();
    private Dictionary<(int Pid, long Created), long> _lastCpu = new();
    private long _lastTimestamp;
    private readonly char[] _pathBuffer = new char[1024];

    public List<ProcessEntry> Scan()
    {
        var windows = WindowFinder.MainWindowsByProcess();
        long now = Stopwatch.GetTimestamp();
        double elapsed100ns = _lastTimestamp == 0 ? 0 : (now - _lastTimestamp) * 1e7 / Stopwatch.Frequency;
        int cores = Environment.ProcessorCount;
        int self = Environment.ProcessId;

        var identities = new Dictionary<(int, long), Identity>();
        var cpuTimes = new Dictionary<(int, long), long>();
        var result = new List<ProcessEntry>(256);

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            try
            {
                int pid = process.Id;
                if (pid == self) continue;

                string processName = process.ProcessName;
                long memory = 0;
                try { memory = process.WorkingSet64; } catch { /* process exited mid-scan */ }

                long created = 0;
                long? cpuTime = null;
                Identity? identity = null;

                IntPtr handle = pid > 4
                    ? Win32.OpenProcess(Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid)
                    : IntPtr.Zero;

                if (handle != IntPtr.Zero)
                {
                    try
                    {
                        if (Win32.GetProcessTimes(handle, out created, out _, out long kernel, out long user))
                            cpuTime = kernel + user;

                        var key = (pid, created);
                        if (!_identities.TryGetValue(key, out identity))
                            identity = Describe(QueryPath(handle), processName, pid);
                        identities[key] = identity;
                    }
                    finally
                    {
                        Win32.CloseHandle(handle);
                    }
                }

                identity ??= Describe(null, processName, pid);

                double? cpu = null;
                if (cpuTime is long time)
                {
                    var key = (pid, created);
                    cpuTimes[key] = time;
                    if (elapsed100ns > 0 && _lastCpu.TryGetValue(key, out long previous))
                        cpu = Math.Clamp((time - previous) / (elapsed100ns * cores) * 100.0, 0, 100);
                }

                windows.TryGetValue(pid, out var window);
                result.Add(new ProcessEntry
                {
                    Pid = pid,
                    Name = identity.Name,
                    ImageName = identity.ImageName,
                    Path = identity.Path,
                    Window = window.Handle,
                    WindowTitle = window.Title ?? "",
                    Memory = memory,
                    Cpu = cpu,
                });
            }
            catch (InvalidOperationException)
            {
                // The process exited while we were reading it.
            }
        }

        _identities = identities;
        _lastCpu = cpuTimes;
        _lastTimestamp = now;
        return result;
    }

    private string? QueryPath(IntPtr handle)
    {
        uint size = (uint)_pathBuffer.Length;
        return Win32.QueryFullProcessImageName(handle, 0, _pathBuffer, ref size)
            ? new string(_pathBuffer, 0, (int)size)
            : null;
    }

    private static Identity Describe(string? path, string processName, int pid)
    {
        if (pid == 0) return new Identity(null, "System Idle Process", "Idle");
        if (pid == 4) return new Identity(null, "System", "System");

        string image = path is null ? processName : System.IO.Path.GetFileName(path);
        string name = processName;
        if (path is not null)
        {
            try
            {
                string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) name = description.Trim();
            }
            catch
            {
                // No version resource or no access; keep the process name.
            }
        }
        return new Identity(path, name, image);
    }
}
