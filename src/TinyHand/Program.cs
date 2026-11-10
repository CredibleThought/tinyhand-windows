using static TinyHand.Native;

namespace TinyHand;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // `TinyHand.exe --quit` closes a running copy (the installer uses it).
        if (args.Contains("--quit"))
        {
            PostMessage(HWND_BROADCAST, TrayApp.QuitMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        // `TinyHand.exe --settings` opens the settings window, starting TinyHand if needed.
        var settings = args.Contains("--settings");

        using var mutex = new Mutex(true, @"Local\TinyHand-Windows", out var first);
        if (!first)
        {
            // Already running: starting it again turns the pointer on.
            PostMessage(HWND_BROADCAST, settings ? TrayApp.SettingsMessage : TrayApp.ShowMessage, IntPtr.Zero, IntPtr.Zero);
            return;
        }

        // If a previous run ended without restoring the cursors, restore them now.
        SystemCursors.Show();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => SystemCursors.Show();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SystemCursors.Show();
        Application.ThreadException += (_, e) =>
        {
            SystemCursors.Show();
            MessageBox.Show(e.Exception.ToString(), "TinyHand error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        };

        ApplicationConfiguration.Initialize();
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var app = new TrayApp();
        if (settings)
            app.OpenSettings();
        Application.Run(app);
        SystemCursors.Show();
    }
}
