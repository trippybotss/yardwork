using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Yardwork.Native;
using Yardwork.Processes;

namespace Yardwork.Overlay;

internal sealed record OverlayTarget(int Pid, string Name, string ImageName, IntPtr Window);

internal enum OverlayState
{
    Starting,
    Idle,
    Attached,
    WaitingForWindow,
    TargetMinimized,
    TargetInBackground,
    Disabled,
    Error,
}

internal enum HotkeyAction
{
    ToggleOverlay = 1,
    ShowMenu = 2,
    Exit = 3,
}

internal sealed record HotkeyInfo(HotkeyAction Action, string Keys, string Description, bool Registered);

/// <summary>
/// Runs the overlay on its own thread: a borderless, click-through, always-on-top window that
/// follows the target process's main window, rendered with Direct3D 11 every vertical blank.
/// The UI thread talks to it only through <see cref="Attach"/>, <see cref="Detach"/>, the shared
/// <see cref="AppSettings"/> and a few read-only properties; events come back on the UI thread.
/// </summary>
internal sealed class OverlayHost : IDisposable
{
    private const string WindowClass = "Yardwork.OverlayWindow";

    // Kept in a static field so the delegate handed to Win32 is never garbage collected.
    private static readonly Win32.WndProc WindowProcedure = WndProc;

    public static readonly IReadOnlyList<(HotkeyAction Action, uint VirtualKey, string Keys, string Description)> HotkeyDefinitions =
    [
        (HotkeyAction.ToggleOverlay, 0x77, "Ctrl+Shift+F8", "Show / hide overlay"),
        (HotkeyAction.ShowMenu, 0x78, "Ctrl+Shift+F9", "Bring Yardwork to the front"),
        (HotkeyAction.Exit, 0x79, "Ctrl+Shift+F10", "Exit Yardwork"),
    ];

    private readonly AppSettings _settings;
    private readonly SynchronizationContext _ui;
    private readonly object _gate = new();
    private OverlayTarget? _pendingTarget;
    private bool _hasPending;
    private OverlayTarget? _publishedTarget;
    private Thread? _thread;
    private volatile bool _stopping;
    private volatile int _state;
    private volatile float _fps;
    private volatile HotkeyInfo[] _hotkeys = [];
    private volatile string? _lastError;

    // Everything below is touched only by the overlay thread.
    private readonly OverlayScene _scene = new();
    private readonly SystemStats _stats = new();
    private IntPtr _hwnd;
    private Renderer? _renderer;
    private OverlayTarget? _target;
    private IntPtr _targetWindow;
    private IntPtr _targetProcess;
    private bool _targetHadWindow;
    private bool _noWindowMode;
    private bool _shown;
    private Win32.RECT _bounds;
    private long _lastTopmost;
    private long _lastWindowSearch;
    private long _lastAliveCheck;
    private long _attachedAt;

    public OverlayHost(AppSettings settings, SynchronizationContext ui)
    {
        _settings = settings;
        _ui = ui;
    }

    public event Action? ShowMenuRequested;
    public event Action? ExitRequested;
    public event Action? OverlayToggled;
    public event Action<OverlayTarget>? TargetExited;

    public OverlayState State => (OverlayState)_state;
    public float Fps => _fps;
    public OverlayTarget? Target => Volatile.Read(ref _publishedTarget);
    public IReadOnlyList<HotkeyInfo> Hotkeys => _hotkeys;
    public string? LastError => _lastError;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Yardwork overlay" };
        _thread.Start();
    }

    public void Attach(OverlayTarget target)
    {
        lock (_gate)
        {
            _pendingTarget = target;
            _hasPending = true;
        }
        Volatile.Write(ref _publishedTarget, target);
    }

    public void Detach()
    {
        lock (_gate)
        {
            _pendingTarget = null;
            _hasPending = true;
        }
        Volatile.Write(ref _publishedTarget, null);
    }

    public void Dispose()
    {
        _stopping = true;
        _thread?.Join(TimeSpan.FromSeconds(2));
    }

    // ---- overlay thread ---------------------------------------------------------------------

    private void Run()
    {
        try
        {
            try
            {
                Win32.SetThreadDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            }
            catch (EntryPointNotFoundException)
            {
                Log.Write("Per-monitor DPI awareness is unavailable on this Windows version.");
            }

            CreateOverlayWindow();
            _renderer = new Renderer(_hwnd, 1, 1);
            if (_renderer.IsSoftware) Log.Write("No hardware Direct3D 11 device; using WARP.");
            _scene.CreateResources(_renderer);
            RegisterHotkeys();
            SetState(OverlayState.Idle);

            long fpsWindowStart = Stopwatch.GetTimestamp();
            int frames = 0;

            while (!_stopping)
            {
                if (!PumpMessages()) break;
                ApplyPendingTarget();

                if (!UpdatePlacement())
                {
                    _fps = 0;
                    frames = 0;
                    fpsWindowStart = Stopwatch.GetTimestamp();
                    Win32.MsgWaitForMultipleObjects(0, null, false, 50, Win32.QS_ALLINPUT);
                    continue;
                }

                _stats.Update(_targetProcess);
                RenderFrame();

                frames++;
                long now = Stopwatch.GetTimestamp();
                double seconds = (now - fpsWindowStart) / (double)Stopwatch.Frequency;
                if (seconds >= 0.5)
                {
                    _fps = (float)(frames / seconds);
                    frames = 0;
                    fpsWindowStart = now;
                }
            }
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Log.Error("overlay", ex);
            SetState(OverlayState.Error);
        }
        finally
        {
            Cleanup();
        }
    }

    private void CreateOverlayWindow()
    {
        IntPtr instance = Win32.GetModuleHandle(null);
        var windowClass = new Win32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WindowProcedure),
            hInstance = instance,
            lpszClassName = WindowClass,
        };
        if (Win32.RegisterClassEx(ref windowClass) == 0)
        {
            int error = Marshal.GetLastWin32Error();
            if (error != Win32.ERROR_CLASS_ALREADY_EXISTS) throw new Win32Exception(error, "RegisterClassEx failed");
        }

        // Layered + transparent = mouse clicks pass straight through to the window underneath.
        const int exStyle = Win32.WS_EX_TOPMOST | Win32.WS_EX_LAYERED | Win32.WS_EX_TRANSPARENT
                            | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        _hwnd = Win32.CreateWindowEx(exStyle, WindowClass, "Yardwork Overlay", Win32.WS_POPUP,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateWindowEx failed");

        Win32.SetLayeredWindowAttributes(_hwnd, 0, 255, Win32.LWA_ALPHA);

        // "Sheet of glass": DWM composites the whole client area using the back buffer's alpha.
        var margins = new Win32.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Win32.DwmExtendFrameIntoClientArea(_hwnd, ref margins);
    }

    private static IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam) => msg switch
    {
        Win32.WM_NCHITTEST => new IntPtr(Win32.HTTRANSPARENT),
        Win32.WM_MOUSEACTIVATE => new IntPtr(Win32.MA_NOACTIVATE),
        _ => Win32.DefWindowProc(hwnd, msg, wParam, lParam),
    };

    private void RegisterHotkeys()
    {
        var hotkeys = new List<HotkeyInfo>();
        foreach (var (action, virtualKey, keys, description) in HotkeyDefinitions)
        {
            bool registered = Win32.RegisterHotKey(_hwnd, (int)action,
                Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, virtualKey);
            if (!registered) Log.Write($"Hotkey {keys} is already taken by another program.");
            hotkeys.Add(new HotkeyInfo(action, keys, description, registered));
        }
        _hotkeys = hotkeys.ToArray();
    }

    private bool PumpMessages()
    {
        while (Win32.PeekMessage(out var msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
        {
            if (msg.message == Win32.WM_QUIT) return false;
            if (msg.message == Win32.WM_HOTKEY)
            {
                OnHotkey((HotkeyAction)msg.wParam.ToInt32());
                continue;
            }
            Win32.TranslateMessage(ref msg);
            Win32.DispatchMessage(ref msg);
        }
        return true;
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleOverlay:
                _settings.OverlayEnabled = !_settings.OverlayEnabled;
                Post(OverlayToggled);
                break;
            case HotkeyAction.ShowMenu:
                Post(ShowMenuRequested);
                break;
            case HotkeyAction.Exit:
                Post(ExitRequested);
                break;
        }
    }

    private void Post(Action? handler)
    {
        if (handler is not null) _ui.Post(_ => handler(), null);
    }

    private void ApplyPendingTarget()
    {
        OverlayTarget? next;
        lock (_gate)
        {
            if (!_hasPending) return;
            next = _pendingTarget;
            _hasPending = false;
        }

        ReleaseTarget();
        if (next is null)
        {
            SetState(OverlayState.Idle);
            return;
        }

        _target = next;
        _targetWindow = next.Window != IntPtr.Zero && Win32.IsWindow(next.Window) ? next.Window : IntPtr.Zero;
        _targetHadWindow = next.Window != IntPtr.Zero;
        _targetProcess = Win32.OpenProcess(Win32.SYNCHRONIZE | Win32.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)next.Pid);
        _stats.Reset();
        _attachedAt = Stopwatch.GetTimestamp();
        _bounds = default;
        _lastWindowSearch = 0;
        _lastAliveCheck = 0;
        Log.Write($"Attached to {next.ImageName} (PID {next.Pid}).");
    }

    private void ReleaseTarget()
    {
        if (_targetProcess != IntPtr.Zero)
        {
            Win32.CloseHandle(_targetProcess);
            _targetProcess = IntPtr.Zero;
        }
        _target = null;
        _targetWindow = IntPtr.Zero;
        _targetHadWindow = false;
        HideOverlay();
    }

    /// <summary>Moves, shows or hides the overlay window. Returns true when a frame should be drawn.</summary>
    private bool UpdatePlacement()
    {
        var target = _target;
        if (target is null)
        {
            HideOverlay();
            SetState(OverlayState.Idle);
            return false;
        }

        long now = Stopwatch.GetTimestamp();
        if (HasTargetExited(target, now))
        {
            Log.Write($"{target.ImageName} (PID {target.Pid}) exited.");
            ReleaseTarget();
            Interlocked.CompareExchange(ref _publishedTarget, null, target);
            SetState(OverlayState.Idle);
            var handler = TargetExited;
            if (handler is not null) _ui.Post(_ => handler(target), null);
            return false;
        }

        if (!_settings.OverlayEnabled)
        {
            HideOverlay();
            SetState(OverlayState.Disabled);
            return false;
        }

        if (_targetWindow != IntPtr.Zero && !Win32.IsWindow(_targetWindow))
            _targetWindow = IntPtr.Zero;

        // The window can be recreated (fullscreen toggle, launcher -> game); look for a new one.
        if (_targetWindow == IntPtr.Zero && Seconds(_lastWindowSearch, now) > 0.5)
        {
            _lastWindowSearch = now;
            if (WindowFinder.FindMainWindow(target.Pid) is { } found)
            {
                _targetWindow = found.Handle;
                _targetHadWindow = true;
            }
        }

        Win32.RECT rect;
        if (_targetWindow != IntPtr.Zero)
        {
            _noWindowMode = false;
            if (Win32.IsIconic(_targetWindow))
            {
                HideOverlay();
                SetState(OverlayState.TargetMinimized);
                return false;
            }
            if (_settings.HideWhenUnfocused && !IsTargetInForeground(target.Pid))
            {
                HideOverlay();
                SetState(OverlayState.TargetInBackground);
                return false;
            }
            if (!TryGetTargetRect(_targetWindow, out rect))
            {
                HideOverlay();
                SetState(OverlayState.WaitingForWindow);
                return false;
            }
        }
        else if (_targetHadWindow)
        {
            HideOverlay();
            SetState(OverlayState.WaitingForWindow);
            return false;
        }
        else
        {
            // Background process with no window: cover the primary monitor instead.
            _noWindowMode = true;
            rect = PrimaryMonitorRect();
        }

        ShowAt(rect, now);
        SetState(OverlayState.Attached);
        return true;
    }

    private void ShowAt(in Win32.RECT rect, long now)
    {
        const uint showFlags = Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW;
        if (!rect.SameAs(_bounds) || !_shown)
        {
            _bounds = rect;
            _renderer!.Resize(rect.Width, rect.Height);
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height, showFlags);
            _shown = true;
            _lastTopmost = now;
        }
        else if (Seconds(_lastTopmost, now) > 1.0)
        {
            // Other topmost windows (and some games) can push us down; re-assert once a second.
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            _lastTopmost = now;
        }
    }

    private void HideOverlay()
    {
        if (!_shown) return;
        Win32.ShowWindow(_hwnd, Win32.SW_HIDE);
        _shown = false;
    }

    private void RenderFrame()
    {
        var renderer = _renderer!;
        uint dpi = 96;
        try { dpi = Win32.GetDpiForWindow(_hwnd); } catch (EntryPointNotFoundException) { }
        float scale = dpi > 0 ? dpi / 96f : 1f;

        var frame = new OverlayFrame(_target!, renderer.Width, renderer.Height, scale, _noWindowMode, _fps, _stats,
            Seconds(_attachedAt, Stopwatch.GetTimestamp()), _settings);

        renderer.BeginFrame();
        _scene.Draw(renderer.D2D, frame);
        if (!renderer.EndFrame()) RecreateRenderer();
    }

    private void RecreateRenderer()
    {
        Log.Write("Direct3D device lost; recreating it.");
        _scene.ReleaseResources();
        _renderer?.Dispose();
        _renderer = new Renderer(_hwnd, Math.Max(1, _bounds.Width), Math.Max(1, _bounds.Height));
        _scene.CreateResources(_renderer);
    }

    private bool TryGetTargetRect(IntPtr hwnd, out Win32.RECT rect)
    {
        rect = default;
        if (_settings.UseClientArea)
        {
            if (!Win32.GetClientRect(hwnd, out var client)) return false;
            var origin = new Win32.POINT();
            if (!Win32.ClientToScreen(hwnd, ref origin)) return false;
            rect = new Win32.RECT
            {
                Left = origin.X,
                Top = origin.Y,
                Right = origin.X + client.Right,
                Bottom = origin.Y + client.Bottom,
            };
        }
        else if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<Win32.RECT>()) != 0
                 && !Win32.GetWindowRect(hwnd, out rect))
        {
            return false;
        }

        return rect.Width > 0 && rect.Height > 0;
    }

    private bool HasTargetExited(OverlayTarget target, long now)
    {
        if (_targetProcess != IntPtr.Zero)
            return Win32.WaitForSingleObject(_targetProcess, 0) == Win32.WAIT_OBJECT_0;

        // Couldn't open a handle (protected process); poll by PID instead.
        if (Seconds(_lastAliveCheck, now) < 1.0) return false;
        _lastAliveCheck = now;
        try
        {
            using var process = Process.GetProcessById(target.Pid);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsTargetInForeground(int pid)
    {
        IntPtr foreground = Win32.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return true;
        Win32.GetWindowThreadProcessId(foreground, out uint foregroundPid);
        // Keep the overlay up while Yardwork itself is focused so settings changes show live.
        return (int)foregroundPid == pid || (int)foregroundPid == Environment.ProcessId;
    }

    private static Win32.RECT PrimaryMonitorRect()
    {
        IntPtr monitor = Win32.MonitorFromWindow(IntPtr.Zero, Win32.MONITOR_DEFAULTTOPRIMARY);
        var info = new Win32.MONITORINFO { cbSize = (uint)Marshal.SizeOf<Win32.MONITORINFO>() };
        return Win32.GetMonitorInfo(monitor, ref info)
            ? info.rcMonitor
            : new Win32.RECT { Right = 1920, Bottom = 1080 };
    }

    private static double Seconds(long start, long now) => (now - start) / (double)Stopwatch.Frequency;

    private void SetState(OverlayState state) => _state = (int)state;

    private void Cleanup()
    {
        try
        {
            if (_hwnd != IntPtr.Zero)
                foreach (var definition in HotkeyDefinitions)
                    Win32.UnregisterHotKey(_hwnd, (int)definition.Action);

            _scene.ReleaseResources();
            _renderer?.Dispose();
            _renderer = null;
            ReleaseTarget();

            if (_hwnd != IntPtr.Zero)
            {
                Win32.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Log.Error("overlay cleanup", ex);
        }
    }
}
