using System.Text.Json;
using System.Text.Json.Serialization;

namespace Yardwork;

/// <summary>
/// User settings. The UI thread writes these and the overlay thread reads them every frame;
/// each value is a single bool/int so reads and writes are atomic.
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool OverlayEnabled { get; set; } = true;
    public bool ShowBorder { get; set; } = true;
    public bool ShowInfoPanel { get; set; } = true;
    public bool ShowPerfHud { get; set; } = true;
    public bool ShowClock { get; set; }
    public bool HideWhenUnfocused { get; set; } = true;
    public bool UseClientArea { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public int AccentArgb { get; set; } = unchecked((int)0xFF7C3AED);

    [JsonIgnore]
    public Color Accent
    {
        get => Color.FromArgb(AccentArgb);
        set => AccentArgb = Color.FromArgb(255, value.R, value.G, value.B).ToArgb();
    }

    public static string FilePath => Path.Combine(Log.Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("load settings", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Log.Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("save settings", ex);
        }
    }

    public void ResetToDefaults()
    {
        var defaults = new AppSettings();
        OverlayEnabled = defaults.OverlayEnabled;
        ShowBorder = defaults.ShowBorder;
        ShowInfoPanel = defaults.ShowInfoPanel;
        ShowPerfHud = defaults.ShowPerfHud;
        ShowClock = defaults.ShowClock;
        HideWhenUnfocused = defaults.HideWhenUnfocused;
        UseClientArea = defaults.UseClientArea;
        AlwaysOnTop = defaults.AlwaysOnTop;
        AccentArgb = defaults.AccentArgb;
    }
}
