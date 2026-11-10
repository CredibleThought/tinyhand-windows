using System.Drawing.Drawing2D;

namespace TinyHand;

/// <summary>One drawn mark: a freehand pen line or a shape from start to end.</summary>
internal sealed class Stroke
{
    public required string Kind;
    public required float Width;
    public required Color Color;
    public List<PointF> Points = new();
    public PointF Start, End;
    public bool Constrain;
    public double FadeAt = double.PositiveInfinity;
    public GraphicsPath? Path;
}

/// <summary>Builds the outline of a stroke. Shared by the real ink and the toolbar's shape icons.</summary>
internal static class StrokePath
{
    private const double ArrowSpread = 0.45; // radians either side of the shaft

    /// <summary>
    /// Where a shape really ends. With <paramref name="constrain"/> (Shift held)
    /// ellipses and rectangles become circles and squares, and arrows snap to 45°.
    /// </summary>
    public static PointF ShapeEnd(string kind, PointF start, PointF end, bool constrain)
    {
        if (!constrain)
            return end;
        double dx = end.X - start.X, dy = end.Y - start.Y;
        if (kind == "arrow")
        {
            const double step = Math.PI / 4;
            var angle = Math.Round(Math.Atan2(dy, dx) / step) * step;
            var len = Math.Sqrt(dx * dx + dy * dy);
            return new PointF((float)(start.X + len * Math.Cos(angle)), (float)(start.Y + len * Math.Sin(angle)));
        }
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new PointF((float)(start.X + (dx < 0 ? -side : side)), (float)(start.Y + (dy < 0 ? -side : side)));
    }

    /// <summary>The two outer points of an arrowhead pointing at <paramref name="end"/>.</summary>
    public static (PointF, PointF) ArrowHead(PointF start, PointF end, float width)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        var len = Math.Sqrt(dx * dx + dy * dy);
        var head = Math.Min(Math.Max(width * 4, 14), len * 0.4);
        var angle = Math.Atan2(dy, dx);
        PointF Side(double a) => new((float)(end.X - head * Math.Cos(angle + a)), (float)(end.Y - head * Math.Sin(angle + a)));
        return (Side(-ArrowSpread), Side(ArrowSpread));
    }

    public static GraphicsPath Build(Stroke s)
    {
        var path = new GraphicsPath();
        if (s.Kind == "pen")
        {
            AddPen(path, s.Points);
            return path;
        }
        var start = s.Start;
        var end = ShapeEnd(s.Kind, s.Start, s.End, s.Constrain);
        switch (s.Kind)
        {
            case "ellipse":
                path.AddEllipse(RectOf(start, end));
                break;
            case "rectangle":
                path.AddRectangle(RectOf(start, end));
                break;
            case "arrow":
                var (a, b) = ArrowHead(start, end, s.Width);
                path.AddLine(start, end);
                path.StartFigure();
                path.AddLines(new[] { a, end, b });
                break;
        }
        return path;
    }

    private static RectangleF RectOf(PointF a, PointF b) =>
        RectangleF.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
            Math.Max(Math.Max(a.X, b.X), Math.Min(a.X, b.X) + 0.5f), Math.Max(Math.Max(a.Y, b.Y), Math.Min(a.Y, b.Y) + 0.5f));

    // Smooths the points with quadratic curves through the midpoints, as the
    // macOS app does; GDI+ only has cubic curves, so raise each one.
    private static void AddPen(GraphicsPath path, List<PointF> points)
    {
        if (points.Count == 1)
        {
            path.AddLine(points[0], new PointF(points[0].X + 0.01f, points[0].Y));
            return;
        }
        var current = points[0];
        for (var i = 1; i < points.Count; i++)
        {
            var prev = points[i - 1];
            var mid = new PointF((prev.X + points[i].X) / 2, (prev.Y + points[i].Y) / 2);
            var c1 = new PointF(current.X + 2f / 3 * (prev.X - current.X), current.Y + 2f / 3 * (prev.Y - current.Y));
            var c2 = new PointF(mid.X + 2f / 3 * (prev.X - mid.X), mid.Y + 2f / 3 * (prev.Y - mid.Y));
            path.AddBezier(current, c1, c2, mid);
            current = mid;
        }
        path.AddLine(current, points[^1]);
    }
}

/// <summary>
/// Drawing on the screen: pen strokes and shapes that fade away after a while.
///
/// The stroke being drawn has its own window, sized to the stroke. When the
/// button comes up the stroke moves to a window of its own, which fades by
/// changing the window's opacity, so finished strokes are never redrawn.
/// </summary>
internal sealed class Ink : IDisposable
{
    private const double FadeSeconds = 0.6;
    private const float MinStep = 1.5f;
    private const float ShadowDx = 1f, ShadowDy = 1.5f;
    private const int ShadowAlpha = 46; // 0.18

    private Stroke? _live;
    private readonly LayeredWindow _liveWindow = new();
    private readonly List<(Stroke Stroke, LayeredWindow Window)> _frozen = new();
    private bool _dirty;

    /// <summary>The monitor's top-left corner; stroke points are relative to it.</summary>
    public Point Origin { get; set; }

    public bool IsDrawing => _live != null;

    /// <summary>All of Ink's windows, bottom first, for keeping them in order.</summary>
    public IEnumerable<LayeredWindow> Windows => _frozen.Select(f => f.Window).Append(_liveWindow);

    public void Begin(float x, float y, float width, Color color, string kind)
    {
        _live = new Stroke { Kind = kind, Width = width, Color = color };
        if (kind == "pen")
            _live.Points.Add(new PointF(x, y));
        else
            _live.Start = _live.End = new PointF(x, y);
        _dirty = true;
    }

    public void Extend(float x, float y, bool constrain)
    {
        var s = _live;
        if (s == null)
            return;
        if (s.Kind != "pen")
        {
            if (s.End.X == x && s.End.Y == y && s.Constrain == constrain)
                return;
            s.End = new PointF(x, y);
            s.Constrain = constrain;
            _dirty = true;
            return;
        }
        var last = s.Points[^1];
        if (Math.Abs(x - last.X) + Math.Abs(y - last.Y) < MinStep)
            return;
        s.Points.Add(new PointF(x, y));
        _dirty = true;
    }

    /// <summary>Finishes the stroke; it fades after <paramref name="fadeAfter"/> seconds, or stays if that is 0.</summary>
    public void End(double fadeAfter, double now)
    {
        var s = _live;
        if (s == null)
            return;
        _live = null;
        _dirty = false;
        _liveWindow.Hide();
        if (fadeAfter > 0)
            s.FadeAt = now + Math.Max(0.5, fadeAfter);
        s.Path = StrokePath.Build(s);
        var window = new LayeredWindow();
        if (Paint(window, s, s.Path))
        {
            window.Commit();
            window.BringToTop();
            _frozen.Add((s, window));
        }
        else
        {
            window.Dispose();
            s.Path.Dispose();
        }
    }

    public void Clear()
    {
        _live = null;
        _dirty = false;
        _liveWindow.Hide();
        foreach (var (s, w) in _frozen)
        {
            s.Path?.Dispose();
            w.Dispose();
        }
        _frozen.Clear();
    }

    /// <summary>Redraws the stroke being drawn and fades finished ones. Returns true while anything is fading.</summary>
    public bool Update(double now)
    {
        if (_dirty && _live != null)
        {
            _dirty = false;
            using var path = StrokePath.Build(_live);
            if (Paint(_liveWindow, _live, path))
                _liveWindow.Commit();
            else
                _liveWindow.Hide();
        }

        var fading = false;
        for (var i = _frozen.Count - 1; i >= 0; i--)
        {
            var (s, w) = _frozen[i];
            if (now < s.FadeAt)
                continue;
            var t = (now - s.FadeAt) / FadeSeconds;
            if (t >= 1)
            {
                s.Path?.Dispose();
                w.Dispose();
                _frozen.RemoveAt(i);
                continue;
            }
            fading = true;
            w.Commit((byte)Math.Round(255 * (1 - t * t)));
        }
        return fading;
    }

    // Draws the stroke into a window sized to it. False if it is off screen.
    private bool Paint(LayeredWindow window, Stroke s, GraphicsPath path)
    {
        var b = path.GetBounds();
        var pad = s.Width / 2 + Math.Max(ShadowDx, ShadowDy) + 2;
        var box = Rectangle.FromLTRB((int)Math.Floor(b.Left - pad), (int)Math.Floor(b.Top - pad),
            (int)Math.Ceiling(b.Right + pad), (int)Math.Ceiling(b.Bottom + pad));
        if (box.Width <= 0 || box.Height <= 0)
            return false;
        var g = window.Begin(new Rectangle(Origin.X + box.X, Origin.Y + box.Y, box.Width, box.Height));
        g.TranslateTransform(-box.X, -box.Y);
        using var pen = new Pen(Color.FromArgb(ShadowAlpha, 0, 0, 0), s.Width)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round,
        };
        var state = g.Save();
        g.TranslateTransform(ShadowDx, ShadowDy);
        g.DrawPath(pen, path);
        g.Restore(state);
        pen.Color = s.Color;
        g.DrawPath(pen, path);
        return true;
    }

    public void Dispose()
    {
        Clear();
        _liveWindow.Dispose();
    }
}
