using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TinyHand;

/// <summary>Where the hand's key points are, as fractions of the 1024x1536 source canvas.</summary>
internal sealed record HandGeometry(double HandTop, double Tip, double Wrist, double RodEnd, double TipX, double RodX,
    double HandMinX, double HandMaxX)
{
    public const double Aspect = HandImporter.Width / (double)HandImporter.Height;
}

/// <summary>
/// Turns a picture of a hand holding a stick into a TinyHand hand: removes a
/// white background, fits it to the canvas the built-in hands use, measures
/// where the fingertip, wrist and stick are, and saves the pieces. A port of
/// the Linux repo's tools/cutout-white.py and tools/prepare-assets.py.
/// </summary>
internal static class HandImporter
{
    public const int Width = 1024, Height = 1536;
    private const int OutWidth = 512, Thumb = 96;
    private const int White = 244;      // every channel at or above this counts as background
    private const int MinRegion = 1500; // smaller white areas are highlights on the hand, not background
    private const int Edge = 200;       // edge pixels fade from opaque (at Edge) to clear (at 255)

    /// <summary>Loads a picture and returns it cut out and fitted to the 1024x1536 canvas.</summary>
    public static Bitmap Prepare(string path)
    {
        using var original = Assets.File(path);
        // Work at about the canvas size: big photos would be slow and gain nothing.
        var scale = Math.Min(1.0, Math.Min(Width * 1.5 / original.Width, Height * 1.5 / original.Height));
        using var working = Resize(original, (int)Math.Round(original.Width * scale), (int)Math.Round(original.Height * scale));
        if (!HasTransparentBackground(working))
            Cutout(working);
        return Fit(working);
    }

    private static Bitmap Resize(Bitmap source, int w, int h)
    {
        var result = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.DrawImage(source, 0, 0, result.Width, result.Height);
        return result;
    }

    // --- pixels -------------------------------------------------------------

    private static byte[] Read(Bitmap image)
    {
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var bytes = new byte[image.Width * image.Height * 4];
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(data.Scan0 + y * data.Stride, bytes, y * image.Width * 4, image.Width * 4);
        image.UnlockBits(data);
        return bytes;
    }

    private static void Write(Bitmap image, byte[] bytes)
    {
        var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        for (var y = 0; y < image.Height; y++)
            Marshal.Copy(bytes, y * image.Width * 4, data.Scan0 + y * data.Stride, image.Width * 4);
        image.UnlockBits(data);
    }

    /// <summary>True when the corners are see-through, so there is no background to remove.</summary>
    private static bool HasTransparentBackground(Bitmap image)
    {
        var px = Read(image);
        int w = image.Width, h = image.Height;
        var corners = new[] { 0, w - 1, (h - 1) * w, h * w - 1 };
        return corners.Count(i => px[i * 4 + 3] < 128) >= 3;
    }

    /// <summary>
    /// Makes large white areas transparent, including white gaps enclosed by
    /// the hand, keeps soft edges, and takes the white out of edge colours.
    /// </summary>
    private static void Cutout(Bitmap image)
    {
        int w = image.Width, h = image.Height, n = w * h;
        var px = Read(image);
        var lo = new byte[n];
        var white = new bool[n];
        for (var i = 0; i < n; i++)
        {
            lo[i] = Math.Min(px[i * 4], Math.Min(px[i * 4 + 1], px[i * 4 + 2]));
            white[i] = lo[i] >= White;
        }

        // Connected white regions big enough to be background.
        var minRegion = MinRegion * (double)n / (Width * Height);
        var background = new bool[n];
        var seen = new bool[n];
        var queue = new int[n];
        var region = new List<int>();
        for (var start = 0; start < n; start++)
        {
            if (!white[start] || seen[start])
                continue;
            region.Clear();
            int head = 0, tail = 0;
            queue[tail++] = start;
            seen[start] = true;
            while (head < tail)
            {
                var i = queue[head++];
                region.Add(i);
                int x = i % w, y = i / w;
                if (x > 0) Visit(i - 1);
                if (x < w - 1) Visit(i + 1);
                if (y > 0) Visit(i - w);
                if (y < h - 1) Visit(i + w);
            }
            if (region.Count >= minRegion)
                foreach (var i in region)
                    background[i] = true;

            void Visit(int j)
            {
                if (white[j] && !seen[j])
                {
                    seen[j] = true;
                    queue[tail++] = j;
                }
            }
        }

        // A band three pixels wide around the background fades by how white it is.
        var band = (bool[])background.Clone();
        for (var step = 0; step < 3; step++)
        {
            var grown = (bool[])band.Clone();
            for (var i = 0; i < n; i++)
            {
                if (band[i])
                    continue;
                int x = i % w, y = i / w;
                if ((x > 0 && band[i - 1]) || (x < w - 1 && band[i + 1]) || (y > 0 && band[i - w]) || (y < h - 1 && band[i + w]))
                    grown[i] = true;
            }
            band = grown;
        }

        for (var i = 0; i < n; i++)
        {
            double alpha = background[i] ? 0 : band[i] ? Math.Clamp((255.0 - lo[i]) / (255 - Edge), 0, 1) : 1;
            var a = Math.Max(alpha, 1e-3);
            for (var c = 0; c < 3; c++)
                px[i * 4 + c] = (byte)Math.Round(Math.Clamp((px[i * 4 + c] - 255 * (1 - a)) / a, 0, 255));
            px[i * 4 + 3] = (byte)Math.Round(alpha * 255);
        }
        Write(image, px);
    }

    /// <summary>Scales the picture to the canvas height and centres it, like the built-in hands.</summary>
    private static Bitmap Fit(Bitmap image)
    {
        var canvas = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var scale = Math.Min((double)Height / image.Height, (double)Width / image.Width);
        var w = (int)Math.Round(image.Width * scale);
        var h = (int)Math.Round(image.Height * scale);
        using var g = Graphics.FromImage(canvas);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(image, (Width - w) / 2, 0, w, h);
        return canvas;
    }

    // --- measuring ----------------------------------------------------------

    /// <summary>
    /// Finds the fingertip (the top of the picture), the wrist (where the hand
    /// narrows to the stick) and the stick. Same method as the macOS app.
    /// </summary>
    public static HandGeometry Measure(Bitmap canvas)
    {
        int w = canvas.Width, h = canvas.Height;
        var px = Read(canvas);
        var first = new int[h];
        var last = new int[h];
        var counts = new int[h];
        for (var y = 0; y < h; y++)
        {
            first[y] = -1;
            for (var x = 0; x < w; x++)
            {
                if (px[(y * w + x) * 4 + 3] <= 128)
                    continue;
                if (first[y] < 0)
                    first[y] = x;
                last[y] = x;
                counts[y]++;
            }
        }
        double Center(int y) => first[y] < 0 ? 0.5 : (first[y] + last[y]) / 2.0 / w;

        var top = Array.FindIndex(counts, c => c > 0);
        if (top < 0)
            throw new InvalidDataException("The picture is empty after removing the background.");
        var searchEnd = Math.Min(h, top + (int)(0.45 * h));
        var maxW = counts[top..searchEnd].Max();
        var fist = Enumerable.Range(top, h - top).First(y => counts[y] > 0.6 * maxW);
        var wristIndex = Enumerable.Range(fist, h - fist).Where(y => counts[y] < 0.35 * maxW).DefaultIfEmpty(-1).First();
        if (wristIndex < 0)
            throw new InvalidDataException("Could not find where the hand meets the stick. The hand needs a straight stick below it.");
        var wrist = wristIndex;
        var rodW = counts[Math.Min(h - 1, wrist + h / 50)];
        var lastRow = Array.FindLastIndex(counts, c => c > 0);
        var start = Math.Min(h - 1, wrist + h / 33);
        var handle = Enumerable.Range(start, h - start).Where(y => counts[y] > 1.8 * rodW).DefaultIfEmpty(-1).First();
        var rodEnd = handle >= 0 ? handle : lastRow;
        if (rodEnd <= wrist + h / 50)
            throw new InvalidDataException("The stick below the hand is too short.");

        int minX = w, maxX = 0;
        for (var y = top; y < wrist; y++)
        {
            if (first[y] < 0)
                continue;
            minX = Math.Min(minX, first[y]);
            maxX = Math.Max(maxX, last[y]);
        }

        return new HandGeometry(
            HandTop: Math.Max(0, top - 0.004 * h) / h,
            Tip: (double)top / h,
            Wrist: (double)wrist / h,
            RodEnd: (double)rodEnd / h,
            TipX: Center(Math.Min(h - 1, top + h / 200)),
            RodX: Center((wrist + rodEnd) / 2),
            HandMinX: (double)minX / w,
            HandMaxX: (double)maxX / w);
    }

    /// <summary>Things about the measurements that suggest the picture will not point well.</summary>
    public static string? Warning(HandGeometry g)
    {
        if (g.Wrist - g.HandTop > 0.6)
            return "The hand looks very long. Check the wrist line sits where the hand meets the stick.";
        if (g.Wrist - g.HandTop < 0.08)
            return "The hand looks very small. Check the wrist line sits where the hand meets the stick.";
        if (g.RodEnd - g.Wrist < 0.15)
            return "The stick is short, so the hand may not reach the bottom of the screen.";
        if (Math.Abs(g.TipX - g.RodX) > 0.3)
            return "The fingertip is far to the side of the stick. Check the fingertip marker.";
        return null;
    }

    /// <summary>A colour for the ink, from the hand's own colours.</summary>
    public static Color GuessInk(Bitmap canvas, HandGeometry g)
    {
        var px = Read(canvas);
        int w = canvas.Width, h = canvas.Height;
        double r = 0, gr = 0, b = 0, total = 0;
        for (var y = (int)(g.HandTop * h); y < (int)(g.Wrist * h); y += 2)
        {
            for (var x = 0; x < w; x += 2)
            {
                var i = (y * w + x) * 4;
                if (px[i + 3] < 200)
                    continue;
                double bb = px[i], gg = px[i + 1], rr = px[i + 2];
                var max = Math.Max(rr, Math.Max(gg, bb));
                var min = Math.Min(rr, Math.Min(gg, bb));
                // Favour colourful pixels over greys, highlights and shadows.
                var weight = 0.05 + (max - min) / 255.0;
                r += rr * weight; gr += gg * weight; b += bb * weight; total += weight;
            }
        }
        if (total == 0)
            return Catalog.ParseHex(Catalog.Red);
        var c = Color.FromArgb((int)(r / total), (int)(gr / total), (int)(b / total));
        // Very light ink is hard to see; darken it a little.
        if (c.GetBrightness() > 0.8)
            c = Color.FromArgb(c.R * 3 / 4, c.G * 3 / 4, c.B * 3 / 4);
        return c;
    }

    // --- saving -------------------------------------------------------------

    /// <summary>Saves the hand into its own folder and returns it.</summary>
    public static Hand Save(Bitmap canvas, HandGeometry g, string title, Color ink)
    {
        var id = HandStore.NewId(title);
        var folder = Path.Combine(HandStore.Root, id);
        Directory.CreateDirectory(folder);
        using (var hand = Slice(canvas, g.HandTop, g.Wrist))
            hand.Save(Path.Combine(folder, "hand.png"), ImageFormat.Png);
        using (var rod = Slice(canvas, g.Wrist, g.RodEnd))
            rod.Save(Path.Combine(folder, "rod.png"), ImageFormat.Png);
        using (var thumb = Thumbnail(canvas, g))
            thumb.Save(Path.Combine(folder, "thumb.png"), ImageFormat.Png);
        canvas.Save(Path.Combine(folder, "source.png"), ImageFormat.Png);

        var r = (double v) => Math.Round(v, 5);
        var record = new Hand(id, title, $"#{ink.R:X2}{ink.G:X2}{ink.B:X2}", r(HandGeometry.Aspect), r(g.HandTop), r(g.Tip),
            r(g.Wrist), r(g.RodEnd), r(g.TipX), r(g.RodX), r(g.HandMinX), r(g.HandMaxX));
        File.WriteAllText(Path.Combine(folder, "hand.json"),
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        return record with { Folder = folder };
    }

    private static Bitmap Slice(Bitmap canvas, double y0, double y1)
    {
        int top = (int)Math.Round(y0 * canvas.Height), bottom = (int)Math.Round(y1 * canvas.Height);
        var height = Math.Max(1, bottom - top);
        var outH = Math.Max(1, (int)Math.Round(height * (double)OutWidth / canvas.Width));
        var result = new Bitmap(OutWidth, outH, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        using var edges = new ImageAttributes();
        edges.SetWrapMode(WrapMode.TileFlipXY);
        g.DrawImage(canvas, new Rectangle(0, 0, OutWidth, outH), 0, top, canvas.Width, height, GraphicsUnit.Pixel, edges);
        return result;
    }

    public static Bitmap Thumbnail(Bitmap canvas, HandGeometry m)
    {
        int w = canvas.Width, h = canvas.Height;
        var handW = (m.HandMaxX - m.HandMinX) * w;
        var handH = (m.Wrist - m.HandTop) * h;
        var side = Math.Max(handW, handH) * 1.06;
        var cx = (m.HandMinX + m.HandMaxX) / 2 * w;
        var cy = (m.HandTop + m.Wrist) / 2 * h;
        var box = RectangleF.FromLTRB((float)Math.Max(0, cx - side / 2), (float)Math.Max(0, cy - side / 2),
            (float)Math.Min(w, cx + side / 2), (float)Math.Min(h, cy + side / 2));
        var fit = Math.Min(Thumb / box.Width, Thumb / box.Height);
        var dw = box.Width * fit;
        var dh = box.Height * fit;
        var result = new Bitmap(Thumb, Thumb, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(result);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(canvas, new RectangleF((Thumb - dw) / 2, (Thumb - dh) / 2, dw, dh), box, GraphicsUnit.Pixel);
        return result;
    }

    // --- template -----------------------------------------------------------

    /// <summary>
    /// A built-in hand on a plain white 1024x1536 canvas, for an AI image tool
    /// to restyle. Hands made from it keep the pose TinyHand measures best.
    /// </summary>
    public static Bitmap Template()
    {
        var hand = Catalog.FindHand("lavender") ?? Catalog.Hands[0];
        using var handImage = hand.Image("hand");
        using var rodImage = hand.Image("rod");
        var canvas = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(canvas);
        g.Clear(Color.White);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float top = (float)(hand.HandTop * Height), wrist = (float)(hand.Wrist * Height), rodEnd = (float)(hand.RodEnd * Height);
        g.DrawImage(handImage, new RectangleF(0, top, Width, wrist - top));
        g.DrawImage(rodImage, new RectangleF(0, wrist, Width, rodEnd - wrist));
        return canvas;
    }

    public const string Prompt =
        "Redraw the hand in this image as [describe your hand, for example: a golden robot hand, a furry cat paw, " +
        "a hand made of ice]. Keep exactly the same pose, size and position: the index finger points straight up " +
        "near the top of the image, and the hand grips the top of a straight vertical stick that runs down to the " +
        "bottom edge. Keep the stick plain and thin. Use a plain pure white background with no shadow, floor, " +
        "text or border. Same 2:3 portrait image size.";
}

/// <summary>The folder of hands the user added: one sub-folder per hand.</summary>
internal static class HandStore
{
    /// <summary>%APPDATA%\TinyHand\hands, or a hands folder next to the exe in portable mode.</summary>
    public static string Root => Settings.Portable
        ? Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "hands")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TinyHand", "hands");

    public static List<Hand> Load()
    {
        var hands = new List<Hand>();
        if (!Directory.Exists(Root))
            return hands;
        foreach (var folder in Directory.GetDirectories(Root).OrderBy(Directory.GetCreationTimeUtc))
        {
            try
            {
                var hand = JsonSerializer.Deserialize<Hand>(File.ReadAllText(Path.Combine(folder, "hand.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (hand != null && File.Exists(Path.Combine(folder, "hand.png")) && File.Exists(Path.Combine(folder, "rod.png")))
                    hands.Add(hand with { Id = Path.GetFileName(folder), Folder = folder });
            }
            catch (Exception)
            {
                // A broken folder is skipped rather than stopping TinyHand.
            }
        }
        return hands;
    }

    public static string NewId(string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0)
            slug = "hand";
        if (slug.Length > 24)
            slug = slug[..24].Trim('-');
        var id = $"my-{slug}";
        for (var n = 2; Directory.Exists(Path.Combine(Root, id)); n++)
            id = $"my-{slug}-{n}";
        return id;
    }

    public static void Delete(Hand hand)
    {
        if (hand.Folder != null && Directory.Exists(hand.Folder))
            Directory.Delete(hand.Folder, true);
    }
}
