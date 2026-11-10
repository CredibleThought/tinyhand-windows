using System.Drawing.Drawing2D;

namespace TinyHand;

/// <summary>
/// Short "look here" effects played on double-click: an expanding glow ring
/// or a small burst of sparks. They move with a fast start but stay bright
/// until late and then fade, so they read clearly.
/// </summary>
internal sealed class Effects
{
    private const double GlowSeconds = 0.65;
    private const double SparkleSeconds = 0.85;
    private const int SparkCount = 14;

    private sealed record Dot(
        double Start, double Duration, float X, float Y, float Diameter,
        float FromScale, float ToScale, float Dx, float Dy,
        Color Fill, Color Border, float BorderWidth, Color Glow, float GlowSize);

    private readonly List<Dot> _dots = new();
    private readonly Random _random = new();

    public bool Active => _dots.Count > 0;

    /// <summary>Plays <paramref name="kind"/> at (x, y); <paramref name="size"/> is the monitor height.</summary>
    public void Play(string kind, float x, float y, Color color, float size, double now)
    {
        if (kind == "glow")
            Glow(x, y, color, size, now);
        else if (kind == "sparkle")
            Sparkle(x, y, color, size, now);
    }

    public void Clear() => _dots.Clear();

    private static Color WithAlpha(Color c, double a) => Color.FromArgb((int)(255 * a), c);

    private void Glow(float x, float y, Color color, float size, double now)
    {
        var diameter = MathF.Round(size * 0.14f);
        var border = Math.Max(4, MathF.Round(size * 0.006f));
        _dots.Add(new Dot(now, GlowSeconds, x, y, diameter, 0.1f, 1f, 0, 0,
            WithAlpha(color, 0.22), color, border, WithAlpha(color, 0.7), border * 3));

        // A bright core that flashes and fades a little faster.
        var core = MathF.Round(diameter * 0.32f);
        _dots.Add(new Dot(now, GlowSeconds * 0.55, x, y, core, 0.5f, 1.3f, 0, 0,
            Color.FromArgb(242, 255, 255, 255), Color.Empty, 0, WithAlpha(color, 0.9), core * 0.9f));
    }

    private void Sparkle(float x, float y, Color color, float size, double now)
    {
        var reach = size * 0.11;
        var spark = Math.Max(8, MathF.Round(size * 0.012f));
        var offset = _random.NextDouble() * Math.PI * 2;
        for (var i = 0; i < SparkCount; i++)
        {
            // Evenly spaced with a little jitter, alternating ink colour and white.
            var angle = offset + (double)i / SparkCount * Math.PI * 2 + (_random.NextDouble() - 0.5) * 0.3;
            var distance = reach * (0.7 + _random.NextDouble() * 0.5);
            var fill = i % 2 == 1 ? Color.White : color;
            _dots.Add(new Dot(now, SparkleSeconds * (0.85 + _random.NextDouble() * 0.3), x, y, spark, 1f, 0.45f,
                (float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance),
                fill, Color.Empty, 0, WithAlpha(color, 0.9), spark * 1.4f));
        }

        var flash = MathF.Round((float)reach * 0.55f);
        _dots.Add(new Dot(now, SparkleSeconds * 0.4, x, y, flash, 0.3f, 1.1f, 0, 0,
            Color.FromArgb(242, 255, 255, 255), Color.Empty, 0, WithAlpha(color, 0.9), flash * 0.9f));
    }

    /// <summary>The area the effects can reach over their whole run; empty when there are none.</summary>
    public RectangleF Bounds()
    {
        var box = RectangleF.Empty;
        foreach (var d in _dots)
        {
            var r = (d.Diameter / 2 + d.GlowSize) * Math.Max(d.FromScale, d.ToScale) + 2;
            var b = RectangleF.FromLTRB(Math.Min(d.X, d.X + d.Dx) - r, Math.Min(d.Y, d.Y + d.Dy) - r,
                Math.Max(d.X, d.X + d.Dx) + r, Math.Max(d.Y, d.Y + d.Dy) + r);
            box = box.IsEmpty ? b : RectangleF.Union(box, b);
        }
        return box;
    }

    public void Render(Graphics g, double now)
    {
        _dots.RemoveAll(d => now >= d.Start + d.Duration);
        foreach (var d in _dots)
        {
            var t = Math.Clamp((now - d.Start) / d.Duration, 0, 1);
            var motion = (float)(1 - Math.Pow(1 - t, 3)); // ease out cubic
            var opacity = 1 - t * t;                       // ease in quad
            var scale = d.FromScale + (d.ToScale - d.FromScale) * motion;
            var cx = d.X + d.Dx * motion;
            var cy = d.Y + d.Dy * motion;
            var r = d.Diameter / 2 * scale;
            if (r <= 0.2f)
                continue;

            // The soft halo, like a CSS box-shadow.
            var outer = r + d.GlowSize * scale;
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(cx - outer, cy - outer, outer * 2, outer * 2);
                using var brush = new PathGradientBrush(halo)
                {
                    CenterColor = Fade(d.Glow, opacity),
                    SurroundColors = new[] { Color.FromArgb(0, d.Glow) },
                    FocusScales = new PointF(r / outer, r / outer),
                };
                g.FillPath(brush, halo);
            }

            var circle = new RectangleF(cx - r, cy - r, r * 2, r * 2);
            using (var fill = new SolidBrush(Fade(d.Fill, opacity)))
                g.FillEllipse(fill, circle);
            if (d.BorderWidth > 0)
            {
                var w = d.BorderWidth * scale;
                using var pen = new Pen(Fade(d.Border, opacity), w);
                g.DrawEllipse(pen, cx - r + w / 2, cy - r + w / 2, r * 2 - w, r * 2 - w);
            }
        }
    }

    private static Color Fade(Color c, double opacity) => Color.FromArgb((int)(c.A * opacity), c);
}
