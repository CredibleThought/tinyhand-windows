using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TinyHand;

/// <summary>
/// The hand on a stick (or the laser dot), drawn in its own small window that
/// follows the pointer. Port of ArmView.swift by way of the GNOME version.
///
/// The maths is done in a y-up frame like the original, with the grip held
/// below the bottom edge of the screen and leaning right of the pointer.
/// </summary>
internal sealed class Arm : IDisposable
{
    private const double MinRodFrac = 0.05;
    private const double GripLeanFrac = 0.30;
    private const double GripDepthFrac = 0.12;
    private const double GripFollow = 0.08;
    private const double LiftFrac = 0.06;
    private const double PressDecay = 0.82;
    // The original constants are per tick of a 120 Hz timer; scale them by
    // elapsed time so the feel is the same at any frame rate.
    private const double TickHz = 120;
    private const int ShadowAlpha = 69; // 0.27

    private readonly LayeredWindow _window = new();
    private readonly ImageAttributes _edges = new();

    private Hand? _hand;
    private Bitmap? _handSource, _rodSource;
    // The images at the current size, and their blurred shadows.
    private int _cacheWidth;
    private Bitmap? _handImage, _rodImage, _handShadow, _rodShadow;
    private float _shadowPad;

    private double _press;
    private double? _gripX, _gripTarget;
    private string _pose = "";
    private bool _hidden;
    private bool _showShadow = true;

    public Arm()
    {
        // Repeat edge pixels when sampling, so scaled images have no faint
        // border and the hand meets the rod without a seam.
        _edges.SetWrapMode(WrapMode.TileFlipXY);
    }

    public LayeredWindow Window => _window;
    public double SizeFrac { get; set; } = 0.13;
    public bool Held { get; set; }

    public bool ShowShadow
    {
        get => _showShadow;
        set { _showShadow = value; _pose = ""; }
    }

    public bool PointerHidden
    {
        get => _hidden;
        set
        {
            _hidden = value;
            _pose = "";
            if (value)
                _window.Hide();
        }
    }

    /// <summary>A hand from the catalogue, or null for the laser dot.</summary>
    public void SetStyle(Hand? hand)
    {
        if (hand == _hand && (hand == null || _handSource != null))
            return;
        _handSource?.Dispose();
        _rodSource?.Dispose();
        _handSource = _rodSource = null;
        DropCache();
        _hand = hand;
        if (hand != null)
        {
            _handSource = Assets.Image($"assets/hands/{hand.Id}_hand.png");
            _rodSource = Assets.Image($"assets/hands/{hand.Id}_rod.png");
        }
        _pose = "";
    }

    public void Press() => _press = 1;

    /// <summary>True while an animation is still moving even if the pointer is still.</summary>
    public bool IsSettling =>
        _press > 0.002 || (_gripX is { } x && _gripTarget is { } t && Math.Abs(t - x) > 0.5);

    /// <summary>Forgets the smoothed grip, for example after moving to another monitor.</summary>
    public void Reset()
    {
        _gripX = _gripTarget = null;
        _pose = "";
    }

    public void Hide() => _window.Hide();

    /// <summary>
    /// Points at (cx, cy), local to <paramref name="monitor"/>. dt is the time
    /// since the previous call, in seconds.
    /// </summary>
    public void Aim(double cx, double cy, Rectangle monitor, double dt)
    {
        double W = monitor.Width, H = monitor.Height;
        var steps = dt * TickHz;
        var targetX = Math.Min(Math.Max(cx + GripLeanFrac * W, W * 0.15), W * 1.05);
        _gripX = _gripX is { } gx0 ? gx0 + (targetX - gx0) * (1 - Math.Pow(1 - GripFollow, steps)) : targetX;
        _gripTarget = targetX;
        if (!Held)
            _press *= Math.Pow(PressDecay, steps);
        if (_hidden)
            return;

        if (_hand == null || _handSource == null)
        {
            AimLaser(cx, cy, monitor);
            return;
        }
        var a = _hand;
        var gx = _gripX.Value;
        var gyUp = -GripDepthFrac * H;

        var dx = cx - gx;
        var dy = (H - cy) - gyUp;
        var reach = Math.Sqrt(dx * dx + dy * dy);
        var aimAngle = Math.Atan2(dy, dx);

        var handPts = SizeFrac * H * (1 - 0.06 * _press);
        var s = handPts / (a.Wrist - a.HandTop);
        var widthPts = s * a.Aspect;
        var handTipPts = (a.Wrist - a.Tip) * s;
        var tipOffX = (a.TipX - a.RodX) * widthPts;

        var along = Math.Sqrt(Math.Max(0, reach * reach - tipOffX * tipOffX));
        var rodLen = Math.Max(MinRodFrac * H, along - handTipPts);
        var tipAngle = Math.Atan2(rodLen + handTipPts, tipOffX);
        var rotation = aimAngle - tipAngle;
        var lift = LiftFrac * H * (1 - 0.8 * _press);

        var pose = $"{Math.Round(rotation * 1000)},{Math.Round(rodLen)},{Math.Round(handPts)},{Math.Round(gx)},{Math.Round(lift)},{monitor}";
        if (pose == _pose)
            return;
        _pose = pose;

        var nominalHand = SizeFrac * H;
        EnsureCache(nominalHand / (a.Wrist - a.HandTop) * a.Aspect, nominalHand, Math.Max(1, H / 1080));
        var k = (float)(handPts / nominalHand);

        var total = rodLen + handPts;
        var degrees = (float)(-rotation * 180 / Math.PI);
        var pivot = new PointF((float)gx, (float)(H + GripDepthFrac * H));
        var left = (float)(-a.RodX * widthPts);
        var pad = _showShadow ? _shadowPad * k : 0;
        var shadowOffset = _showShadow ? new PointF((float)(lift * 0.6), (float)lift) : PointF.Empty;

        // The window covers the rotated hand and its shadow, clipped to the monitor.
        var corners = new[]
        {
            new PointF(left - pad, (float)-total - pad), new PointF(left + (float)widthPts + pad, (float)-total - pad),
            new PointF(left - pad, pad), new PointF(left + (float)widthPts + pad, pad),
        };
        using var turn = new Matrix();
        turn.Rotate(degrees);
        turn.TransformPoints(corners);
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in corners)
        {
            foreach (var o in new[] { PointF.Empty, shadowOffset })
            {
                minX = Math.Min(minX, c.X + o.X); maxX = Math.Max(maxX, c.X + o.X);
                minY = Math.Min(minY, c.Y + o.Y); maxY = Math.Max(maxY, c.Y + o.Y);
            }
        }
        var box = Rectangle.FromLTRB(
            (int)Math.Floor(pivot.X + minX), (int)Math.Floor(pivot.Y + minY),
            (int)Math.Ceiling(pivot.X + maxX), (int)Math.Ceiling(pivot.Y + maxY));
        box.Intersect(new Rectangle(0, 0, monitor.Width, monitor.Height));
        if (box.Width <= 0 || box.Height <= 0)
        {
            _window.Hide();
            return;
        }

        var g = _window.Begin(new Rectangle(monitor.X + box.X, monitor.Y + box.Y, box.Width, box.Height));
        var hw = (float)widthPts;
        var hp = (float)handPts;
        var rl = (float)rodLen;
        Matrix Place(PointF offset)
        {
            var m = new Matrix();
            m.Translate(pivot.X - box.X + offset.X, pivot.Y - box.Y + offset.Y);
            m.Rotate(degrees);
            m.Translate(left, (float)-total);
            return m;
        }

        // The shadow falls down and to the right, and tightens as the hand
        // presses toward the screen.
        if (_showShadow && _handShadow != null && _rodShadow != null)
        {
            using var m = Place(shadowOffset);
            g.Transform = m;
            Draw(g, _handShadow, new RectangleF(-pad, -pad, hw + 2 * pad, hp + pad));
            Draw(g, _rodShadow, new RectangleF(-pad, hp, hw + 2 * pad, rl));
        }
        using (var m = Place(PointF.Empty))
        {
            g.Transform = m;
            Draw(g, _handImage!, new RectangleF(0, 0, hw, hp));
            Draw(g, _rodImage!, new RectangleF(0, hp, hw, rl));
        }
        g.ResetTransform();
        _window.Commit();
    }

    private void Draw(Graphics g, Bitmap image, RectangleF dest)
    {
        var points = new[] { dest.Location, new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom) };
        g.DrawImage(image, points, new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel, _edges);
    }

    private void AimLaser(double cx, double cy, Rectangle monitor)
    {
        var H = monitor.Height;
        var r = Math.Max(4, H * 0.0038 * SizeFrac / 0.13) * (1 + 0.2 * _press);
        var rr = (float)(Math.Round(r * 2) / 2);
        var pose = $"laser,{rr},{Math.Round(cx)},{Math.Round(cy)},{monitor}";
        if (pose == _pose)
            return;
        _pose = pose;

        var glow = rr * (2.2f + (float)_press) + rr * 0.4f;
        var outer = rr + glow;
        var size = (int)Math.Ceiling(outer * 2) + 2;
        var x0 = (int)Math.Round(cx - size / 2.0);
        var y0 = (int)Math.Round(cy - size / 2.0);
        var g = _window.Begin(new Rectangle(monitor.X + x0, monitor.Y + y0, size, size));
        var c = new PointF((float)cx - x0, (float)cy - y0);

        using (var halo = new GraphicsPath())
        {
            halo.AddEllipse(c.X - outer, c.Y - outer, outer * 2, outer * 2);
            using var brush = new PathGradientBrush(halo)
            {
                CenterColor = Color.FromArgb(217, 255, 41, 31),
                SurroundColors = new[] { Color.FromArgb(0, 255, 41, 31) },
                FocusScales = new PointF(rr / outer, rr / outer),
            };
            g.FillPath(brush, halo);
        }
        using (var dot = new SolidBrush(Catalog.ParseHex(Catalog.Red)))
            g.FillEllipse(dot, c.X - rr, c.Y - rr, rr * 2, rr * 2);
        var core = rr * 0.45f;
        using (var light = new SolidBrush(Color.FromArgb(242, 255, 219, 209)))
            g.FillEllipse(light, c.X - core, c.Y - core, core * 2, core * 2);
        _window.Commit();
    }

    // Scales the hand and rod once for this size, and makes their shadows.
    private void EnsureCache(double width, double handHeight, double screenScale)
    {
        var w = (int)Math.Round(width);
        if (w == _cacheWidth && _handImage != null)
            return;
        DropCache();
        _cacheWidth = w;
        var hh = Math.Max(1, (int)Math.Round(handHeight));
        var rh = Math.Max(1, (int)Math.Round(_rodSource!.Height * (double)w / _rodSource.Width));
        _handImage = Scale(_handSource!, w, hh);
        _rodImage = Scale(_rodSource, w, rh);

        var radius = (int)Math.Round(5 * screenScale);
        var pad = radius * 3;
        _shadowPad = pad;
        // The hand's shadow continues a little into the rod before blurring,
        // and is then cut at the wrist, so the two shadows meet cleanly.
        _handShadow = Shadow(_handImage, _rodImage, pad, radius);
        _rodShadow = RodShadow(_rodImage, pad, radius);
    }

    private void DropCache()
    {
        foreach (var b in new[] { _handImage, _rodImage, _handShadow, _rodShadow })
            b?.Dispose();
        _handImage = _rodImage = _handShadow = _rodShadow = null;
        _cacheWidth = 0;
    }

    private static Bitmap Scale(Bitmap source, int w, int h)
    {
        var result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(result);
        using var edges = new ImageAttributes();
        edges.SetWrapMode(WrapMode.TileFlipXY);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(source, new Rectangle(0, 0, w, h), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, edges);
        return result;
    }

    private static byte[] Alpha(Bitmap image)
    {
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * image.Height];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        image.UnlockBits(data);
        var alpha = new byte[image.Width * image.Height];
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                alpha[y * image.Width + x] = bytes[y * data.Stride + x * 4 + 3];
        return alpha;
    }

    // Hand shadow: (w + 2 pad) x (h + pad), blurred both ways.
    private static Bitmap Shadow(Bitmap hand, Bitmap rod, int pad, int radius)
    {
        int w = hand.Width, h = hand.Height;
        var handA = Alpha(hand);
        var rodA = Alpha(rod);
        int W = w + 2 * pad, H = h + 2 * pad;
        var buf = new float[W * H];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                buf[(y + pad) * W + x + pad] = handA[y * w + x];
        for (var y = 0; y < Math.Min(pad, rod.Height); y++)
            for (var x = 0; x < w; x++)
                buf[(h + pad + y) * W + x + pad] = rodA[y * w + x];
        for (var pass = 0; pass < 3; pass++)
        {
            BoxBlur(buf, W, H, radius, horizontal: true);
            BoxBlur(buf, W, H, radius, horizontal: false);
        }
        return ToShadow(buf, W, h + pad);
    }

    // Rod shadow: blurred sideways only, so its top meets the hand's cleanly.
    private static Bitmap RodShadow(Bitmap rod, int pad, int radius)
    {
        int w = rod.Width, h = rod.Height, W = w + 2 * pad;
        var rodA = Alpha(rod);
        var buf = new float[W * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                buf[y * W + x + pad] = rodA[y * w + x];
        for (var pass = 0; pass < 3; pass++)
            BoxBlur(buf, W, h, radius, horizontal: true);
        return ToShadow(buf, W, h);
    }

    private static void BoxBlur(float[] buf, int w, int h, int r, bool horizontal)
    {
        int lines = horizontal ? h : w, len = horizontal ? w : h;
        var line = new float[len];
        for (var i = 0; i < lines; i++)
        {
            for (var j = 0; j < len; j++)
                line[j] = buf[horizontal ? i * w + j : j * w + i];
            float sum = 0;
            for (var j = -r; j <= r; j++)
                sum += line[Math.Clamp(j, 0, len - 1)];
            for (var j = 0; j < len; j++)
            {
                buf[horizontal ? i * w + j : j * w + i] = sum / (2 * r + 1);
                sum += line[Math.Min(j + r + 1, len - 1)] - line[Math.Max(j - r, 0)];
            }
        }
    }

    private static Bitmap ToShadow(float[] buf, int w, int h)
    {
        var result = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        var data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        var bytes = new byte[data.Stride * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                bytes[y * data.Stride + x * 4 + 3] = (byte)Math.Clamp(buf[y * w + x] * ShadowAlpha / 255f, 0, 255);
        Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
        result.UnlockBits(data);
        return result;
    }

    public void Dispose()
    {
        DropCache();
        _handSource?.Dispose();
        _rodSource?.Dispose();
        _edges.Dispose();
        _window.Dispose();
    }
}
