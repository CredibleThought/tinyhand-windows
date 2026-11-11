using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static TinyHand.Native;

namespace TinyHand;

/// <summary>
/// The notification-area icon and its menu, the global shortcuts, and the
/// messages a second copy of TinyHand sends to this one.
/// </summary>
internal sealed class TrayApp : ApplicationContext
{
    public static readonly uint ShowMessage = RegisterWindowMessage("TinyHand.Show");
    public static readonly uint SettingsMessage = RegisterWindowMessage("TinyHand.Settings");
    public static readonly uint QuitMessage = RegisterWindowMessage("TinyHand.Quit");

    private const int HotkeyToggle = 1, HotkeyClear = 2, HotkeyExit = 3;

    private readonly Settings _settings = new();
    private readonly Controller _controller;
    private readonly NotifyIcon _icon;
    private readonly ContextMenuStrip _menu = new();
    private readonly MessageWindow _messages;
    private SettingsForm? _settingsForm;
    private Icon? _iconOn, _iconOff;
    private string _iconStyle = "";

    public TrayApp()
    {
        _controller = new Controller(_settings);
        _messages = new MessageWindow(this);
        _icon = new NotifyIcon { Text = "TinyHand", ContextMenuStrip = _menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _controller.Toggle();
        };
        _menu.Opening += (_, _) => BuildMenu();

        _controller.ActiveChanged += OnActiveChanged;
        _settings.Changed += SyncIcon;

        if (!RegisterHotKey(_messages.Handle, HotkeyToggle, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'H'))
            Notify("Ctrl+Alt+H is taken", "Another app uses Ctrl+Alt+H. Click the TinyHand icon to turn the pointer on.");
        RegisterHotKey(_messages.Handle, HotkeyClear, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'X');
        SyncIcon();
    }

    private void Notify(string title, string text) =>
        _icon.ShowBalloonTip(5000, title, text, ToolTipIcon.None);

    private void OnActiveChanged()
    {
        // Esc turns the pointer off, but only claim it while the pointer is on.
        if (_controller.Active)
            RegisterHotKey(_messages.Handle, HotkeyExit, MOD_NOREPEAT, VK_ESCAPE);
        else
            UnregisterHotKey(_messages.Handle, HotkeyExit);
        SyncIcon();
        if (_controller.Active && !_settings.Data.HintShown)
        {
            _settings.Update(d => d.HintShown = true);
            var key = Catalog.TitleOf(Catalog.InteractKeys, _settings.Data.InteractKey);
            var parts = new List<string> { "Click to tap", "drag to draw" };
            if (_settings.Data.InteractKey != "off")
                parts.Add($"hold {key} to click for real");
            parts.Add("Esc to turn it off");
            Notify("TinyHand is on", string.Join(", ", parts) + ".");
        }
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case HotkeyToggle: _controller.Toggle(); break;
            case HotkeyClear: _controller.ClearInk(); break;
            case HotkeyExit: _controller.Deactivate(); break;
        }
    }

    // The icon is the current hand, in colour while the pointer is on and
    // grey while it is off.
    private void SyncIcon()
    {
        if (_iconStyle != _settings.Style)
        {
            _iconStyle = _settings.Style;
            DestroyIcons();
            var size = SystemInformation.SmallIconSize.Width;
            using var colour = StyleImage(_iconStyle, size);
            using var grey = Greyed(colour);
            _iconOn = Icon.FromHandle(colour.GetHicon());
            _iconOff = Icon.FromHandle(grey.GetHicon());
        }
        _icon.Icon = _controller.Active ? _iconOn : _iconOff;
        var name = Settings.Portable ? "TinyHand portable" : "TinyHand";
        _icon.Text = _controller.Active ? $"{name} (on)" : $"{name} (off)";
    }

    private void DestroyIcons()
    {
        foreach (var i in new[] { _iconOn, _iconOff })
        {
            if (i == null) continue;
            DestroyIcon(i.Handle);
            i.Dispose();
        }
        _iconOn = _iconOff = null;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Bitmap StyleImage(string style, int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        if (style == Catalog.LaserId)
        {
            using var red = new SolidBrush(Catalog.ParseHex(Catalog.Red));
            g.FillEllipse(red, size * 0.2f, size * 0.2f, size * 0.6f, size * 0.6f);
        }
        else
        {
            using var thumb = Catalog.FindHand(style)?.Image("thumb") ?? new Bitmap(1, 1);
            g.DrawImage(thumb, 0, 0, size, size);
        }
        return bmp;
    }

    private static Bitmap Greyed(Bitmap source)
    {
        var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        using var attrs = new ImageAttributes();
        attrs.SetColorMatrix(new ColorMatrix(new[]
        {
            new[] { 0.3f, 0.3f, 0.3f, 0, 0 },
            new[] { 0.59f, 0.59f, 0.59f, 0, 0 },
            new[] { 0.11f, 0.11f, 0.11f, 0, 0 },
            new[] { 0f, 0, 0, 0.75f, 0 },
            new[] { 0f, 0, 0, 0, 1 },
        }));
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, attrs);
        return bmp;
    }

    private void BuildMenu()
    {
        var d = _settings.Data;
        _menu.Items.Clear();

        var pointer = new ToolStripMenuItem("Pointer", null, (_, _) => _controller.Toggle())
        {
            Checked = _controller.Active, ShortcutKeyDisplayString = "Ctrl+Alt+H",
        };
        pointer.Font = new Font(pointer.Font, FontStyle.Bold);
        _menu.Items.Add(pointer);
        _menu.Items.Add(new ToolStripMenuItem("Clear drawing", null, (_, _) => _controller.ClearInk())
        {
            ShortcutKeyDisplayString = "Ctrl+Alt+X",
        });
        _menu.Items.Add(new ToolStripSeparator());

        _menu.Items.Add(Choices($"Hand: {Catalog.TitleOf(Catalog.Styles.ToArray(), _settings.Style)}",
            Catalog.Styles, _settings.Style, id => _settings.Update(s => s.PointerStyle = id)));
        _menu.Items.Add(Choices($"Draw: {Catalog.TitleOf(Catalog.Shapes, _settings.Shape)}",
            Catalog.Shapes, _settings.Shape, id => _settings.Update(s => s.DrawShape = id)));
        _menu.Items.Add(Choices($"Ink: {Catalog.InkChoices.First(c => c.Id == d.InkChoice || c.Id == "match").Title}",
            Catalog.InkChoices.Select(c => (c.Id, c.Title)), d.InkChoice, id => _settings.Update(s => s.InkChoice = id)));
        _menu.Items.Add(Choices($"Double-click: {Catalog.TitleOf(Catalog.ClickEffects, _settings.ClickEffect)}",
            Catalog.ClickEffects, _settings.ClickEffect, id => _settings.Update(s => s.DoubleClickEffect = id)));
        _menu.Items.Add(new ToolStripMenuItem("Tap sound", null, (_, _) => _settings.SetSoundOn(!_settings.SoundOn))
        {
            Checked = _settings.SoundOn,
        });

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Add hand…", null, (_, _) => AddHandForm.Run(_settings)));
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));
        // A portable copy may move or run from a removable drive, so it does not offer to start with Windows.
        if (!Settings.Portable)
        {
            _menu.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) => Startup.Enabled = !Startup.Enabled)
            {
                Checked = Startup.Enabled,
            });
        }
        _menu.Items.Add(new ToolStripMenuItem("Quit TinyHand", null, (_, _) => Quit()));
    }

    private static ToolStripMenuItem Choices(string title, IEnumerable<(string Id, string Title)> items, string current,
        Action<string> pick)
    {
        var menu = new ToolStripMenuItem(title);
        foreach (var (id, label) in items)
            menu.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) => pick(id)) { Checked = id == current });
        return menu;
    }

    /// <summary>Opens the Add hand window with a picture already chosen.</summary>
    public void AddHand(string path) => AddHandForm.Run(_settings, null, path);

    public void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings);
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    public void Quit()
    {
        _controller.Dispose();
        DestroyIcons();
        _icon.Visible = false;
        _icon.Dispose();
        _messages.DestroyHandle();
        ExitThread();
    }

    /// <summary>A hidden top-level window: it receives the shortcuts and messages broadcast by a second copy.</summary>
    private sealed class MessageWindow : NativeWindow
    {
        private readonly TrayApp _app;

        public MessageWindow(TrayApp app)
        {
            _app = app;
            CreateHandle(new CreateParams { Caption = "TinyHand messages", ExStyle = WS_EX_TOOLWINDOW });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
                _app.OnHotkey((int)m.WParam);
            else if (m.Msg == (int)ShowMessage)
                _app._controller.Activate();
            else if (m.Msg == (int)SettingsMessage)
                _app.OpenSettings();
            else if (m.Msg == (int)QuitMessage)
                _app.Quit();
            else
                base.WndProc(ref m);
        }
    }
}
