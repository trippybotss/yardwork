using System.Diagnostics;
using System.Runtime.InteropServices;
using Yardwork.Native;

namespace Yardwork.Overlay;

/// <summary>
/// System-wide CPU and memory use, plus CPU and memory of the attached process.
/// These are the same OS counters Task Manager shows. Sampled at most once a second.
/// </summary>
internal sealed class SystemStats
{
    private long _lastSample;
    private long _idle, _kernel, _user;
    private long _processTime;
    private bool _hasProcessTime;

    public float CpuPercent { get; private set; }
    public ulong MemoryUsed { get; private set; }
    public ulong MemoryTotal { get; private set; }

    /// <summary>Negative when the attached process can't be queried.</summary>
    public float TargetCpuPercent { get; private set; } = -1;

    /// <summary>Negative when the attached process can't be queried.</summary>
    public long TargetWorkingSet { get; private set; } = -1;

    public void Reset()
    {
        _hasProcessTime = false;
        TargetCpuPercent = -1;
        TargetWorkingSet = -1;
        _lastSample = 0;
    }

    public void Update(IntPtr process)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastSample != 0 && now - _lastSample < Stopwatch.Frequency) return;
        double elapsed100ns = _lastSample == 0 ? 0 : (now - _lastSample) * 1e7 / Stopwatch.Frequency;

        if (Win32.GetSystemTimes(out long idle, out long kernel, out long user))
        {
            if (_lastSample != 0)
            {
                // Kernel time includes idle time.
                long busyPlusIdle = (kernel - _kernel) + (user - _user);
                long idleDelta = idle - _idle;
                if (busyPlusIdle > 0)
                    CpuPercent = (float)Math.Clamp(100.0 * (busyPlusIdle - idleDelta) / busyPlusIdle, 0, 100);
            }
            _idle = idle;
            _kernel = kernel;
            _user = user;
        }

        var memory = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
        if (Win32.GlobalMemoryStatusEx(ref memory))
        {
            MemoryTotal = memory.ullTotalPhys;
            MemoryUsed = memory.ullTotalPhys - memory.ullAvailPhys;
        }

        if (process != IntPtr.Zero)
        {
            if (Win32.GetProcessTimes(process, out _, out _, out long processKernel, out long processUser))
            {
                long total = processKernel + processUser;
                if (_hasProcessTime && elapsed100ns > 0)
                    TargetCpuPercent = (float)Math.Clamp((total - _processTime) / (elapsed100ns * Environment.ProcessorCount) * 100.0, 0, 100);
                _processTime = total;
                _hasProcessTime = true;
            }

            if (Win32.K32GetProcessMemoryInfo(process, out var counters, (uint)Marshal.SizeOf<Win32.PROCESS_MEMORY_COUNTERS>()))
                TargetWorkingSet = (long)counters.WorkingSetSize;
        }

        _lastSample = now;
    }
}
