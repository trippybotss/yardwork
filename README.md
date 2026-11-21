# Yardwork

A Windows overlay app. Pick any running process from a Task Manager-style list, and Yardwork
draws a click-through Direct3D 11 overlay that stays lined up with that process's window.

- **Processes page:** every running process with its icon, description, PID, CPU, memory and
  window title. Filter by Apps, Background or All, search, and sort by any column.
- **Overlay:** a transparent, always-on-top window that follows the target window as it moves
  and resizes. It hides when the target is minimized or (optionally) when it loses focus.
- **What it draws:** an outline around the target, an info panel, a performance HUD (overlay
  FPS, system CPU and RAM, the target's CPU and memory) and an optional clock.
- **Dark purple UI** with a selectable accent color that applies to the overlay too.

## Build

You need Windows 10 (1607 or later) or Windows 11.

**Visual Studio 2026:** open `Yardwork.sln`, set the configuration to *Release*, and choose
*Build → Build Solution*. The exe ends up in `src/Yardwork/bin/Release/net10.0-windows/`.

**Visual Studio 2022:** open `src/Yardwork/Yardwork.csproj` and change
`<TargetFramework>net10.0-windows</TargetFramework>` to `net8.0-windows`, then build as above.

**Command line** (.NET 10 SDK):

```
dotnet build -c Release
```

For a single exe you can copy to another PC:

```
dotnet publish src/Yardwork -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

NuGet restores the Vortice packages (Direct3D 11, DXGI, Direct2D, DirectWrite) automatically on the first build.

## Use

1. Start `Yardwork.exe`.
2. On **Processes**, select a process and click **Attach Overlay** (or double-click the row).
3. Use **Overlay** to choose what's drawn and the accent color.

| Hotkey | Action |
| --- | --- |
| Ctrl+Shift+F8 | Show / hide the overlay |
| Ctrl+Shift+F9 | Bring the Yardwork window to the front |
| Ctrl+Shift+F10 | Exit |

Other keys inside the app: **Ctrl+F** searches the process list, **F5** refreshes it.

Notes:

- The overlay can't appear above apps in **exclusive fullscreen**. Switch those to borderless
  or windowed mode.
- A process with no window (a background process) gets the overlay across the primary monitor.
- Settings and a log file are kept in `%AppData%\Yardwork`.

## Project layout

```
src/Yardwork/
  Program.cs              startup, single-instance check
  AppSettings.cs          settings shared by the UI and overlay threads, saved as JSON
  Native/Win32.cs         Win32 declarations
  Processes/              process snapshot (ProcessScanner) and main-window lookup (WindowFinder)
  Overlay/OverlayHost.cs  overlay thread: window, tracking the target, hotkeys, render loop
  Overlay/Renderer.cs     D3D11 device + swap chain, Direct2D/DirectWrite on the same back buffer
  Overlay/OverlayScene.cs what gets drawn each frame
  UI/                     main window, theme, custom controls and pages
```

### Adding your own overlay content

Draw in `OverlayScene.Draw`. It receives the Direct2D device context and the frame size; (0,0) is
the top-left of the target window. Add any brushes or text formats you need in
`CreateResources` and release them in `ReleaseResources`, because they're recreated if the GPU
device is lost. For raw Direct3D 11 rendering, `Renderer.Device` and `Renderer.Context` are
available. Draw between `BeginFrame` and `EndFrame` in `OverlayHost.RenderFrame`.

### How the transparency works

The overlay window is `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST` (clicks pass through)
with `DwmExtendFrameIntoClientArea(-1)`, so DWM composites it using the alpha channel of the
back buffer. Each frame clears to transparent `(0,0,0,0)`, so only what you draw is visible.
