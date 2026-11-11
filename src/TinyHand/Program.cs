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

        // `TinyHand.exe --save-template file.png` writes the hand template for AI image tools.
        var template = Array.IndexOf(args, "--save-template");
        if (template >= 0 && template + 1 < args.Length)
        {
            using var image = HandImporter.Template();
            image.Save(args[template + 1], System.Drawing.Imaging.ImageFormat.Png);
            return;
        }

        // `TinyHand.exe --settings` opens the settings window, starting TinyHand if needed.
        var settings = args.Contains("--settings");

        using var mutex = new Mutex(true, @"Local\TinyHand-Windows", out var first);
        if (!first)
        {
            // Already running: starting it again turns the pointer on.
            var message = args.Contains("--add-hand") ? TrayApp.AddHandMessage
                : settings ? TrayApp.SettingsMessage : TrayApp.ShowMessage;
            PostMessage(HWND_BROADCAST, message, IntPtr.Zero, IntPtr.Zero);
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
        // `TinyHand.exe --add-hand [picture.png]` opens the Add hand window, with that picture if given.
        var addHand = Array.IndexOf(args, "--add-hand");
        if (addHand >= 0)
        {
            var picture = addHand + 1 < args.Length ? args[addHand + 1] : null;
            Application.Idle += OpenOnce;
            void OpenOnce(object? sender, EventArgs e)
            {
                Application.Idle -= OpenOnce;
                app.AddHand(picture);
            }
        }
        Application.Run(app);
        SystemCursors.Show();
    }
}
