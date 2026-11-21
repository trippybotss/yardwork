using Yardwork.UI.Controls;

namespace Yardwork.UI.Pages;

/// <summary>What the overlay draws and how it behaves.</summary>
internal sealed class OverlayPage : Page
{
    public OverlayPage(AppSettings settings)
    {
        AddSection("Overlay Settings", Glyphs.View);
        AddToggle("Overlay Enabled", () => settings.OverlayEnabled, v => settings.OverlayEnabled = v);
        AddToggle("Outline Target Window", () => settings.ShowBorder, v => settings.ShowBorder = v);
        AddToggle("Info Panel", () => settings.ShowInfoPanel, v => settings.ShowInfoPanel = v);
        AddToggle("Performance HUD", () => settings.ShowPerfHud, v => settings.ShowPerfHud = v);
        AddToggle("Clock", () => settings.ShowClock, v => settings.ShowClock = v);
        AddToggle("Hide When Target Isn't Focused", () => settings.HideWhenUnfocused, v => settings.HideWhenUnfocused = v);
        AddToggle("Fit To Client Area", () => settings.UseClientArea, v => settings.UseClientArea = v);
        AddParagraph("\"Fit to client area\" lines the overlay up with the window's content and skips its title bar. " +
                     "Turn it off to cover the whole window frame.");

        AddSection("Colors", Glyphs.Palette);
        var picker = new AccentPicker();
        picker.ColorPicked += color =>
        {
            settings.Accent = color;
            Theme.SetAccent(color);
        };
        AddRow("Accent Color", picker);
        AddParagraph("The accent color is used by this window and by the overlay itself.");
    }
}
