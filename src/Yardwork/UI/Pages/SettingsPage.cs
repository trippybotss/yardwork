using System.Diagnostics;
using Yardwork.Overlay;
using Yardwork.UI.Controls;

namespace Yardwork.UI.Pages;

/// <summary>Window behaviour, hotkeys and about text.</summary>
internal sealed class SettingsPage : Page
{
    private readonly Dictionary<HotkeyAction, TagLabel> _hotkeyTags = new();

    public SettingsPage(AppSettings settings)
    {
        AddSection("Window", Glyphs.Settings);
        AlwaysOnTopToggle = AddToggle("Always On Top", () => settings.AlwaysOnTop, v => settings.AlwaysOnTop = v);

        var openFolder = new FlatButton("Open Folder");
        openFolder.Click += (_, _) => OpenDataFolder();
        AddRow("Settings & Log Folder", openFolder);

        var reset = new FlatButton("Reset");
        reset.Click += (_, _) => ResetRequested?.Invoke();
        AddRow("Reset All Settings", reset);

        AddSection("Hotkeys", Glyphs.Keyboard);
        foreach (var (action, _, keys, description) in OverlayHost.HotkeyDefinitions)
        {
            var tag = new TagLabel(keys);
            _hotkeyTags[action] = tag;
            AddRow(description, tag);
        }
        AddParagraph("Hotkeys work from anywhere, even while another app has focus. " +
                     "If one is marked \"in use\", another program already registered that key combination.");

        AddSection("About", Glyphs.Info);
        AddParagraph("Yardwork draws a click-through Direct3D 11 overlay on top of the window you pick. " +
                     "Choose a process on the Processes page, then use the Overlay page to choose what's drawn.");
        AddParagraph("Apps running in exclusive fullscreen take over the display, so nothing can be drawn above them. " +
                     "Switch those to borderless or windowed mode.");
    }

    public event Action? ResetRequested;

    public ToggleSwitch AlwaysOnTopToggle { get; }

    public void UpdateHotkeys(IReadOnlyList<HotkeyInfo> hotkeys)
    {
        foreach (var hotkey in hotkeys)
        {
            if (!_hotkeyTags.TryGetValue(hotkey.Action, out var tag)) continue;
            if (hotkey.Registered) tag.SetText(hotkey.Keys, Theme.AccentBright);
            else tag.SetText($"{hotkey.Keys}  ·  in use", Theme.Danger);
        }
    }

    private static void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(Log.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Log.Directory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("open folder", ex);
        }
    }
}
