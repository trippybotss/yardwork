using Yardwork.Overlay;
using Yardwork.UI;

namespace Yardwork;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\Yardwork.SingleInstance", out bool firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Yardwork is already running.", "Yardwork", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // The UI is system-DPI aware; the overlay thread switches itself to per-monitor awareness
        // so the window rectangles it reads are real pixels on every monitor.
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Error("ui", e.Exception);
            MessageBox.Show(e.Exception.Message, "Yardwork", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var settings = AppSettings.Load();
        Theme.SetAccent(settings.Accent);

        using (var overlay = new OverlayHost(settings, SynchronizationContext.Current!))
        {
            overlay.Start();
            using var form = new MainForm(settings, overlay);
            Application.Run(form);
        }

        settings.Save();
    }
}
