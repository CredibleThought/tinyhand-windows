using System.Drawing.Drawing2D;

namespace TinyHand;

/// <summary>
/// The floating toolbar along the bottom of the screen and the trays that
/// open above it. Port of Toolbar.swift by way of the GNOME version.
///
/// Its windows never take clicks themselves: the mouse hook swallows every
/// click while the pointer is on, and the controller asks <see cref="Click"/>
/// whether the toolbar wants it.
/// </summary>
internal sealed class Toolbar : IDisposable
{
    private const int BottomMargin = 24, TrayGap = 8, TrayColumns = 10;
    private const int NearX = 80, NearAbove = 90, NearPad = 10;
    private const int Button = 40, BarPadX = 10, BarPadY = 8, BarSpacing = 6, RowSpacing = 2, TrayPad = 6;
    private const int ShadowMargin = 16;
    private const double ShowSeconds = 0.12, HideSeconds = 0.15;

    private enum Kind { Style, More, Shape, Ink, Sound, Exit, Separator, Swatch, ShapeChoice }

    private sealed record Item(Kind Kind, string Id, Rectangle Rect);

    private sealed class Panel : IDisposable
    {
        public readonly LayeredWindow Window = new();
        public readonly List<Item> Items = new();
        public Rectangle Bounds;           // the panel itself, in screen pixels
        public bool Visible;
        public bool Dirty = true;
        public double AnimStart, AnimFrom, AnimTo = 1, AnimSeconds = 0.0001;
        public double Opacity(double now) =>
            AnimFrom + (AnimTo - AnimFrom) * Math.Clamp((now - AnimStart) / AnimSeconds, 0, 1);
        public bool Animating(double now) => now < AnimStart + AnimSeconds;
        public void Dispose() => Window.Dispose();
    }

    private readonly Panel _bar = new(), _handTray = new(), _inkTray = new(), _shapeTray = new();
    private readonly Panel[] _trays;
    private readonly Dictionary<string, Bitmap> _thumbs = new();

    public Action<string>? OnPick, OnInk, OnShape;
    public Action? OnToggleSound, OnExit;

    private List<string> _pinned = new();
    private string _style = "", _inkChoice = "match", _shape = "ellipse";
    private Color _inkColor = Color.Red;
    private bool _soundOn = true;
    private Panel? _openTray;
    private Item? _hover;
    private Rectangle _work;
    private double _k = 1;

    public Toolbar()
    {
        _trays = new[] { _handTray, _inkTray, _shapeTray };
    }

    public bool Revealed { get; private set; }
    public bool TrayOpen => _openTray != null;

    private int S(double v) => (int)Math.Round(v * _k);

    // --- state from the controller -------------------------------------

    public void SetPinned(IEnumerable<string> ids)
    {
        var known = Catalog.Styles.Select(s => s.Id).ToHashSet();
        _pinned = ids.Where(known.Contains).ToList();
        Layout();
    }

    public void Select(string style) { _style = style; MarkDirty(); }
    public void SetInk(Color color, string choice) { _inkColor = color; _inkChoice = choice; MarkDirty(); }
    public void SetShape(string shape) { _shape = shape; MarkDirty(); }
    public void SetSoundOn(bool on) { _soundOn = on; MarkDirty(); }

    private void MarkDirty()
    {
        foreach (var p in All)
            p.Dirty = true;
    }

    private IEnumerable<Panel> All => _trays.Prepend(_bar);

    /// <summary>Centres the toolbar along the bottom of the work area (above the taskbar).</summary>
    public void Place(Rectangle workArea, double scale)
    {
        _work = workArea;
        _k = scale;
        foreach (var t in _thumbs.Values)
            t.Dispose();
        _thumbs.Clear();
        Layout();
    }

    private void Layout()
    {
        if (_work.IsEmpty)
            return;
        var m = S(ShadowMargin);

        // The bar: pinned styles, more, separator, shape, ink, sound, exit.
        _bar.Items.Clear();
        var x = m + S(BarPadX);
        var y = m + S(BarPadY);
        foreach (var id in _pinned)
        {
            _bar.Items.Add(new Item(Kind.Style, id, new Rectangle(x, y, S(Button), S(Button))));
            x += S(Button) + S(RowSpacing);
        }
        if (_pinned.Count > 0)
            x += S(BarSpacing) - S(RowSpacing);
        foreach (var kind in new[] { Kind.More, Kind.Separator, Kind.Shape, Kind.Ink, Kind.Sound, Kind.Exit })
        {
            var w = kind == Kind.Separator ? S(9) : S(Button);
            _bar.Items.Add(new Item(kind, "", new Rectangle(x, y, w, S(Button))));
            x += w + S(BarSpacing);
        }
        var barW = x - S(BarSpacing) + S(BarPadX) - m;
        var barH = S(BarPadY) * 2 + S(Button);
        _bar.Bounds = new Rectangle(_work.X + (_work.Width - barW) / 2, _work.Bottom - barH - S(BottomMargin), barW, barH);

        LayoutTray(_handTray, Kind.Style, Catalog.Styles.Select(s => s.Id).ToList(), TrayColumns);
        LayoutTray(_inkTray, Kind.Swatch, Catalog.InkChoices.Select(c => c.Id).ToList(), 99);
        LayoutTray(_shapeTray, Kind.ShapeChoice, Catalog.Shapes.Select(c => c.Id).ToList(), 99);
        MarkDirty();
    }

    private void LayoutTray(Panel tray, Kind kind, List<string> ids, int columns)
    {
        tray.Items.Clear();
        var m = S(ShadowMargin);
        var step = S(Button) + S(RowSpacing);
        var cols = Math.Min(columns, ids.Count);
        var rows = (ids.Count + cols - 1) / cols;
        for (var i = 0; i < ids.Count; i++)
        {
            tray.Items.Add(new Item(kind, ids[i],
                new Rectangle(m + S(TrayPad) + i % cols * step, m + S(TrayPad) + i / cols * step, S(Button), S(Button))));
        }
        var w = S(TrayPad) * 2 + cols * step - S(RowSpacing);
        var h = S(TrayPad) * 2 + rows * step - S(RowSpacing);
        tray.Bounds = new Rectangle(_work.X + (_work.Width - w) / 2, _bar.Bounds.Y - h - S(TrayGap), w, h);
    }

    // --- showing and hiding --------------------------------------------

    public void SetRevealed(bool revealed, double now)
    {
        if (revealed == Revealed)
            return;
        Revealed = revealed;
        if (!revealed)
            CloseTrays(now);
        Fade(_bar, revealed ? 1 : 0, revealed ? ShowSeconds : HideSeconds, now);
    }

    private static void Fade(Panel p, double to, double seconds, double now)
    {
        p.AnimFrom = p.Opacity(now);
        p.AnimTo = to;
        p.AnimStart = now;
        p.AnimSeconds = seconds;
        if (to > 0)
        {
            p.Visible = true;
            p.Dirty = true;
        }
    }

    private void ToggleTray(Panel tray, double now)
    {
        var open = _openTray != tray;
        CloseTrays(now);
        if (!open)
            return;
        _openTray = tray;
        tray.AnimFrom = 0;
        Fade(tray, 1, ShowSeconds, now);
        tray.AnimFrom = 0;
        _bar.Dirty = true;
    }

    public void CloseTrays(double now = 0)
    {
        if (_openTray == null)
            return;
        _openTray.Visible = false;
        _openTray.Window.Hide();
        _openTray = null;
        _bar.Dirty = true;
    }

    /// <summary>Hides everything at once, when the pointer turns off.</summary>
    public void Hide()
    {
        Revealed = false;
        CloseTrays();
        foreach (var p in All)
        {
            p.Visible = false;
            p.AnimFrom = p.AnimTo = 0;
            p.Window.Hide();
        }
    }

    public void BringToTop()
    {
        foreach (var p in All)
            if (p.Visible)
                p.Window.BringToTop();
    }

    /// <summary>True when (x, y) is close enough to the toolbar that it should show.</summary>
    public bool IsNear(Point p)
    {
        if (_bar.Bounds.IsEmpty)
            return false;
        var b = _bar.Bounds;
        if (p.X >= b.X - S(NearX) && p.X <= b.Right + S(NearX) &&
            p.Y >= b.Y - S(NearAbove) - S(NearPad) && p.Y <= b.Bottom + S(NearPad))
            return true;
        if (_openTray != null)
        {
            var t = _openTray.Bounds;
            t.Inflate(S(NearPad), S(NearPad));
            return t.Contains(p);
        }
        return false;
    }

    /// <summary>True when (x, y) is on a visible panel (so the click belongs to the toolbar).</summary>
    public bool Contains(Point p) =>
        (Revealed && _bar.Bounds.Contains(p)) || (_openTray != null && _openTray.Bounds.Contains(p));

    private (Panel, Item)? ItemAt(Point p)
    {
        foreach (var panel in new[] { _openTray, Revealed ? _bar : null })
        {
            if (panel == null || !panel.Bounds.Contains(p))
                continue;
            var local = new Point(p.X - panel.Bounds.X + S(ShadowMargin), p.Y - panel.Bounds.Y + S(ShadowMargin));
            foreach (var item in panel.Items)
                if (item.Kind != Kind.Separator && item.Rect.Contains(local))
                    return (panel, item);
            return null;
        }
        return null;
    }

    /// <summary>Handles a click at (x, y). Returns false if the toolbar is not there.</summary>
    public bool Click(Point p, double now)
    {
        if (!Contains(p))
            return false;
        if (ItemAt(p) is not { } hit)
            return true;
        var item = hit.Item2;
        switch (item.Kind)
        {
            case Kind.Style: CloseTrays(); OnPick?.Invoke(item.Id); break;
            case Kind.More: ToggleTray(_handTray, now); break;
            case Kind.Shape: ToggleTray(_shapeTray, now); break;
            case Kind.Ink: ToggleTray(_inkTray, now); break;
            case Kind.Sound: OnToggleSound?.Invoke(); break;
            case Kind.Exit: OnExit?.Invoke(); break;
            case Kind.Swatch: CloseTrays(); OnInk?.Invoke(item.Id); break;
            case Kind.ShapeChoice: CloseTrays(); OnShape?.Invoke(item.Id); break;
        }
        return true;
    }

    /// <summary>Redraws what changed and runs the fades. Returns true while still animating.</summary>
    public bool Update(Point cursor, double now)
    {
        var hover = ItemAt(cursor)?.Item2;
        if (hover != _hover)
        {
            _hover = hover;
            MarkDirty();
        }
        var animating = false;
        foreach (var p in All)
        {
            if (!p.Visible)
                continue;
            var opacity = p.Opacity(now);
            var moving = p.Animating(now);
            animating |= moving;
            if (!moving && opacity <= 0)
            {
                p.Visible = false;
                p.Window.Hide();
                continue;
            }
            if (p.Dirty)
            {
                Render(p);
                p.Dirty = false;
            }
            else if (!moving && p.Window.Shown)
            {
                continue;
            }
            p.Window.Commit((byte)Math.Round(255 * opacity));
        }
        return animating;
    }

    // --- drawing ---------------------------------------------------------

    private void Render(Panel p)
    {
        var m = S(ShadowMargin);
        var outer = new Rectangle(p.Bounds.X - m, p.Bounds.Y - m, p.Bounds.Width + 2 * m, p.Bounds.Height + 2 * m);
        var g = p.Window.Begin(outer);
        var body = new Rectangle(m, m, p.Bounds.Width, p.Bounds.Height);
        var radius = S(p == _bar ? 16 : 14);

        // A soft drop shadow.
        for (var i = 1; i <= m; i += Math.Max(1, m / 8))
        {
            var r = body;
            r.Inflate(i, i);
            r.Offset(0, S(4));
            using var shadowPath = Rounded(r, radius + i);
            using var shadow = new SolidBrush(Color.FromArgb((int)(22.0 * (1 - (double)i / m)), 0, 0, 0));
            g.FillPath(shadow, shadowPath);
        }
        using (var path = Rounded(body, radius))
        {
            using var fill = new SolidBrush(Color.FromArgb(p == _bar ? 224 : 235, 28, 28, 32));
            g.FillPath(fill, path);
            using var border = new Pen(Color.FromArgb(26, 255, 255, 255), Math.Max(1, S(1)));
            g.DrawPath(border, path);
        }

        foreach (var item in p.Items)
        {
            if (item.Kind == Kind.Separator)
            {
                using var line = new SolidBrush(Color.FromArgb(46, 255, 255, 255));
                g.FillRectangle(line, item.Rect.X + S(4), item.Rect.Y + S(6), Math.Max(1, S(1)), item.Rect.Height - S(12));
                continue;
            }
            var selected = IsSelected(item);
            if (selected || item == _hover)
            {
                using var hl = Rounded(item.Rect, S(10));
                using var brush = new SolidBrush(Color.FromArgb(selected ? 56 : 31, 255, 255, 255));
                g.FillPath(brush, hl);
            }
            DrawIcon(g, item);
        }
    }

    private bool IsSelected(Item item) => item.Kind switch
    {
        Kind.Style => item.Id == _style,
        Kind.More => _openTray == _handTray || !_pinned.Contains(_style),
        Kind.Shape => _openTray == _shapeTray,
        Kind.Ink => _openTray == _inkTray,
        Kind.Swatch => item.Id == _inkChoice,
        Kind.ShapeChoice => item.Id == _shape,
        _ => false,
    };

    private void DrawIcon(Graphics g, Item item)
    {
        var c = new PointF(item.Rect.X + item.Rect.Width / 2f, item.Rect.Y + item.Rect.Height / 2f);
        switch (item.Kind)
        {
            case Kind.Style:
                if (item.Id == Catalog.LaserId)
                    DrawDot(g, c, Catalog.ParseHex(Catalog.Red));
                else
                    g.DrawImage(Thumb(item.Id), c.X - S(16), c.Y - S(16), S(32), S(32));
                break;
            case Kind.More:
                using (var white = new SolidBrush(Color.White))
                {
                    var cell = S(5); var gap = S(2);
                    var start = -(cell * 3 + gap * 2) / 2f;
                    for (var r = 0; r < 3; r++)
                        for (var col = 0; col < 3; col++)
                        {
                            using var sq = Rounded(new Rectangle((int)(c.X + start + col * (cell + gap)),
                                (int)(c.Y + start + r * (cell + gap)), cell, cell), S(1));
                            g.FillPath(white, sq);
                        }
                }
                break;
            case Kind.Shape:
                DrawShapeIcon(g, c, _shape);
                break;
            case Kind.ShapeChoice:
                DrawShapeIcon(g, c, item.Id);
                break;
            case Kind.Ink:
                DrawDot(g, c, _inkColor);
                break;
            case Kind.Swatch:
                var color = Catalog.InkChoices.First(i => i.Id == item.Id).Color;
                DrawDot(g, c, color == null ? null : Catalog.ParseHex(color));
                break;
            case Kind.Sound:
                DrawSpeaker(g, c, _soundOn);
                break;
            case Kind.Exit:
                using (var pen = new Pen(Color.White, Math.Max(1.5f, S(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    var d = S(6);
                    g.DrawLine(pen, c.X - d, c.Y - d, c.X + d, c.Y + d);
                    g.DrawLine(pen, c.X + d, c.Y - d, c.X - d, c.Y + d);
                }
                break;
        }
    }

    /// <summary>A round colour dot; null draws the "match the pointer" gradient.</summary>
    private void DrawDot(Graphics g, PointF c, Color? color)
    {
        var r = S(9);
        var rect = new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);
        Brush brush = color is { } solid
            ? new SolidBrush(solid)
            : new LinearGradientBrush(rect, Catalog.ParseHex(Catalog.Red), Catalog.ParseHex("#0A84FF"), 0f);
        using (brush)
            g.FillEllipse(brush, rect);
        var w = Math.Max(1.5f, S(2));
        using var pen = new Pen(Color.FromArgb(217, 255, 255, 255), w);
        g.DrawEllipse(pen, rect.X + w / 2, rect.Y + w / 2, rect.Width - w, rect.Height - w);
    }

    private static readonly Dictionary<string, Stroke> ShapeSamples = new()
    {
        ["pen"] = new Stroke
        {
            Kind = "pen", Width = 1.5f, Color = Color.White,
            Points = new() { new(3, 14), new(6, 8), new(9, 11), new(12, 6), new(15, 10), new(17, 5) },
        },
        ["ellipse"] = new Stroke { Kind = "ellipse", Width = 1.5f, Color = Color.White, Start = new(2, 5), End = new(18, 15) },
        ["rectangle"] = new Stroke { Kind = "rectangle", Width = 1.5f, Color = Color.White, Start = new(3, 5), End = new(17, 15) },
        ["arrow"] = new Stroke { Kind = "arrow", Width = 1.5f, Color = Color.White, Start = new(3, 17), End = new(17, 3) },
    };

    private void DrawShapeIcon(Graphics g, PointF c, string kind)
    {
        if (!ShapeSamples.TryGetValue(kind, out var sample))
            return;
        using var path = StrokePath.Build(sample);
        var state = g.Save();
        g.TranslateTransform(c.X - S(10), c.Y - S(10));
        g.ScaleTransform((float)_k, (float)_k);
        using var pen = new Pen(Color.White, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
        g.Restore(state);
    }

    private void DrawSpeaker(Graphics g, PointF c, bool on)
    {
        var u = (float)_k;
        var x = c.X - 8 * u;
        using var white = new SolidBrush(Color.White);
        g.FillPolygon(white, new[]
        {
            new PointF(x, c.Y - 3 * u), new PointF(x + 3.5f * u, c.Y - 3 * u), new PointF(x + 8 * u, c.Y - 7 * u),
            new PointF(x + 8 * u, c.Y + 7 * u), new PointF(x + 3.5f * u, c.Y + 3 * u), new PointF(x, c.Y + 3 * u),
        });
        using var pen = new Pen(Color.White, 1.6f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (on)
        {
            g.DrawArc(pen, x + 6 * u, c.Y - 5 * u, 8 * u, 10 * u, -50, 100);
            g.DrawArc(pen, x + 6 * u, c.Y - 8.5f * u, 13 * u, 17 * u, -50, 100);
        }
        else
        {
            g.DrawLine(pen, x + 11 * u, c.Y - 4 * u, x + 17 * u, c.Y + 4 * u);
            g.DrawLine(pen, x + 17 * u, c.Y - 4 * u, x + 11 * u, c.Y + 4 * u);
        }
    }

    private Bitmap Thumb(string id)
    {
        if (_thumbs.TryGetValue(id, out var cached))
            return cached;
        using var source = Catalog.FindHand(id)?.Image("thumb") ?? new Bitmap(1, 1);
        var size = S(32);
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, 0, 0, size, size);
        }
        return _thumbs[id] = bmp;
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 1)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        foreach (var p in All)
            p.Dispose();
        foreach (var t in _thumbs.Values)
            t.Dispose();
    }
}
