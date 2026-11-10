using System.Diagnostics;
using static TinyHand.Native;

namespace TinyHand;

/// <summary>
/// Runs the pointer while it is on.
///
/// How it fits together:
///  - A low-level mouse hook swallows clicks, so a click becomes a tap (or the
///    start of a drawing) and nothing underneath is clicked. Moving and
///    scrolling still reach the app under the pointer.
///  - The system cursor is hidden and the hand is drawn in a small
///    click-through window that follows it.
///  - Ink and effects share one click-through window over the monitor, and
///    the toolbar has its own windows at the bottom.
///  - A timer polls the pointer every frame and aims the hand at it.
/// </summary>
internal sealed class Controller : IDisposable
{
    private const double RevealOnStart = 3.0;
    private const double RevealOnMonitor = 1.5;
    private const int DrawThreshold = 4;
    private const int DoubleClickDistance = 12;

    private readonly Settings _settings;
    private readonly Arm _arm = new();
    private readonly Ink _ink = new();
    private readonly Effects _effects = new();
    private readonly Toolbar _toolbar = new();
    private readonly LayeredWindow _fxWindow = new();
    private readonly TapSound _sound = new();
    private readonly MouseHook _hook;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 8 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Rectangle _monitor = Rectangle.Empty;
    private Rectangle _work;
    private double _scale = 1;
    private bool _normal;
    private double _revealUntil, _lastTime, _lastRaise;
    private (int X, int Y, MouseButtons Button)? _down;
    private (uint Time, int X, int Y)? _lastClick;

    public Controller(Settings settings)
    {
        _settings = settings;
        _hook = new MouseHook(OnMouse);
        _timer.Tick += (_, _) => Tick();

        _toolbar.OnPick = id => _settings.Update(d => d.PointerStyle = id);
        _toolbar.OnInk = id => _settings.Update(d => d.InkChoice = id);
        _toolbar.OnShape = id => _settings.Update(d => d.DrawShape = id);
        _toolbar.OnToggleSound = () => _settings.SetSoundOn(!_settings.SoundOn);
        _toolbar.OnExit = Deactivate;

        _settings.Changed += Apply;
        Apply();
    }

    public bool Active { get; private set; }

    /// <summary>Raised when the pointer turns on or off.</summary>
    public event Action? ActiveChanged;

    private double Now => _clock.Elapsed.TotalSeconds;

    private void Apply()
    {
        var d = _settings.Data;
        _arm.SetStyle(_settings.IsLaser ? null : Catalog.FindHand(_settings.Style));
        _arm.SizeFrac = d.PointerSize;
        _arm.ShowShadow = d.ShowShadow;
        _hook.InteractKey = d.InteractKey;
        _toolbar.SetPinned(d.PinnedStyles);
        _toolbar.Select(_settings.Style);
        _toolbar.SetInk(_settings.InkColor, d.InkChoice);
        _toolbar.SetShape(_settings.Shape);
        _toolbar.SetSoundOn(_settings.SoundOn);
    }

    public void Toggle()
    {
        if (Active) Deactivate(); else Activate();
    }

    public void Activate()
    {
        if (Active)
            return;
        Active = true;
        _monitor = Rectangle.Empty;
        _normal = false;
        _lastTime = 0;
        _arm.Reset();
        _arm.PointerHidden = false;
        _revealUntil = Now + RevealOnStart;
        _hook.Active = true;
        SystemCursors.Hide();
        timeBeginPeriod(1);
        Tick();
        _timer.Start();
        ActiveChanged?.Invoke();
    }

    public void Deactivate()
    {
        if (!Active)
            return;
        Active = false;
        _hook.Active = false;
        _timer.Stop();
        timeEndPeriod(1);
        _down = null;
        _arm.Held = false;
        _lastClick = null;
        _effects.Clear();
        _ink.Clear();
        _fxWindow.Hide();
        _arm.Hide();
        _toolbar.Hide();
        SystemCursors.Show();
        ActiveChanged?.Invoke();
    }

    public void ClearInk()
    {
        _ink.Clear();
        _effects.Clear();
        _fxWindow.Hide();
    }

    private void UseMonitor(Screen screen)
    {
        var moving = !_monitor.IsEmpty;
        _monitor = screen.Bounds;
        _work = screen.WorkingArea;
        _scale = ScaleAt(_monitor.X + _monitor.Width / 2, _monitor.Y + _monitor.Height / 2);
        if (moving)
        {
            Release();
            _revealUntil = Now + RevealOnMonitor;
        }
        _ink.Clear();
        _ink.Origin = _monitor.Location;
        _effects.Clear();
        _fxWindow.Hide();
        _arm.Reset();
        _toolbar.Place(_work, _scale);
        Raise();
    }

    // Keep TinyHand's windows above other always-on-top windows, in order:
    // ink, then the toolbar, then the hand.
    private void Raise()
    {
        foreach (var w in _ink.Windows)
            w.BringToTop();
        _fxWindow.BringToTop();
        _toolbar.BringToTop();
        _arm.Window.BringToTop();
        _lastRaise = Now;
    }

    private void OnMouse(MouseEvent e)
    {
        if (!Active)
            return;
        var now = Now;
        var p = new Point(e.X, e.Y);
        if (e.Action == MouseAction.Up)
        {
            if (_down is { } down && down.Button == e.Button)
                Release();
            return;
        }
        if (_down != null)
            return;
        if (_toolbar.Click(p, now))
            return;

        _arm.Press();
        _arm.Held = true;
        if (_settings.SoundOn)
            _sound.Play(_settings.Data.TapSound);
        _toolbar.CloseTrays();
        _down = (e.X, e.Y, e.Button);
        if (IsDoubleClick(e))
            PlayClickEffect(e.X, e.Y, now);
        Tick();
    }

    private bool IsDoubleClick(MouseEvent e)
    {
        if (e.Button != MouseButtons.Left)
        {
            _lastClick = null;
            return false;
        }
        var distance = DoubleClickDistance * _scale;
        var isDouble = _lastClick is { } last &&
            unchecked(e.Time - last.Time) <= GetDoubleClickTime() &&
            Math.Abs(e.X - last.X) <= distance && Math.Abs(e.Y - last.Y) <= distance;
        // A third click starts a new pair rather than playing again.
        _lastClick = isDouble ? null : (e.Time, e.X, e.Y);
        return isDouble;
    }

    private void PlayClickEffect(int x, int y, double now)
    {
        var kind = _settings.ClickEffect;
        if (_monitor.IsEmpty || kind == "off")
            return;
        _effects.Play(kind, x - _monitor.X, y - _monitor.Y, _settings.InkColor, _monitor.Height, now);
    }

    private void Release()
    {
        _down = null;
        _arm.Held = false;
        if (_ink.IsDrawing)
        {
            _ink.End(_settings.Data.FadeSeconds, Now);
            Raise();
        }
    }

    private void Drag(Point p)
    {
        if (_down is not { } down || down.Button != MouseButtons.Left || _monitor.IsEmpty)
            return;
        if (!_ink.IsDrawing)
        {
            if (Math.Abs(p.X - down.X) + Math.Abs(p.Y - down.Y) < DrawThreshold * _scale)
                return;
            var width = (float)Math.Max(3, _monitor.Height * 0.0055);
            _ink.Begin(down.X - _monitor.X, down.Y - _monitor.Y, width, _settings.InkColor, _settings.Shape);
        }
        // Shift makes circles, squares and 45° arrows.
        _ink.Extend(p.X - _monitor.X, p.Y - _monitor.Y, KeyDown(VK_SHIFT));
    }

    private void Tick()
    {
        if (!Active)
            return;
        var now = Now;
        var dt = _lastTime > 0 ? Math.Min(0.1, now - _lastTime) : 1.0 / 60;
        _lastTime = now;

        GetCursorPos(out var pt);
        var p = new Point(pt.X, pt.Y);
        var screen = Screen.FromPoint(p);
        if (screen.Bounds != _monitor)
            UseMonitor(screen);

        var drawing = _ink.IsDrawing;
        var interacting = MouseHook.InteractHeld(_settings.Data.InteractKey);
        // Over the taskbar or one of TinyHand's menus the real pointer shows
        // and clicks go through, like the menu bar on macOS.
        var elsewhere = _down == null && (!_work.Contains(p) || OwnWindowAt(p.X, p.Y));
        var normal = !drawing && (interacting || elsewhere);
        if (normal != _normal)
        {
            _normal = normal;
            _arm.PointerHidden = normal;
            if (normal) SystemCursors.Show(); else SystemCursors.Hide();
        }

        var near = _toolbar.IsNear(p);
        _toolbar.SetRevealed(!drawing && (near || now < _revealUntil || _toolbar.TrayOpen), now);

        if (_down != null)
            Drag(p);

        var t0 = _clock.Elapsed.TotalMilliseconds;
        _arm.Aim(p.X - _monitor.X, p.Y - _monitor.Y, _monitor, dt);
        var t1 = _clock.Elapsed.TotalMilliseconds;

        _ink.Update(now);
        if (_effects.Active)
            RenderEffects(now);
        else
            _fxWindow.Hide();
        var t2 = _clock.Elapsed.TotalMilliseconds;

        _toolbar.Update(p, now);
        var t3 = _clock.Elapsed.TotalMilliseconds;
        Profile.Add(t1 - t0, t2 - t1, t3 - t2, t0 - now * 1000);

        if (now - _lastRaise > 1)
            Raise();
    }

    private void RenderEffects(double now)
    {
        var area = _effects.Bounds();
        var box = Rectangle.Intersect(Rectangle.FromLTRB((int)Math.Floor(area.Left), (int)Math.Floor(area.Top),
            (int)Math.Ceiling(area.Right), (int)Math.Ceiling(area.Bottom)), new Rectangle(Point.Empty, _monitor.Size));
        if (box.Width <= 0 || box.Height <= 0)
        {
            _fxWindow.Hide();
            return;
        }
        var g = _fxWindow.Begin(new Rectangle(_monitor.X + box.X, _monitor.Y + box.Y, box.Width, box.Height));
        g.TranslateTransform(-box.X, -box.Y);
        _effects.Render(g, now);
        _fxWindow.Commit();
    }

    public void Dispose()
    {
        Deactivate();
        _settings.Changed -= Apply;
        _timer.Dispose();
        _hook.Dispose();
        _arm.Dispose();
        _toolbar.Dispose();
        _ink.Dispose();
        _fxWindow.Dispose();
        _sound.Dispose();
    }
}
