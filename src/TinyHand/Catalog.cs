using System.Reflection;
using System.Text.Json;

namespace TinyHand;

/// <summary>One hand style. Positions are fractions of the source image, written by the asset tool.</summary>
internal sealed record Hand(string Id, string Title, string Ink, double Aspect, double HandTop, double Tip,
    double Wrist, double RodEnd, double TipX, double RodX);

/// <summary>The fixed lists of choices: hands, ink colours, shapes, effects and sounds.</summary>
internal static class Catalog
{
    public const string Red = "#FF291F";
    public const string LaserId = "laser";
    public const string LaserTitle = "Laser dot";

    public static readonly (string Id, string Title, string? Color)[] InkChoices =
    {
        ("match", "Match the pointer", null),
        ("red", "Red", Red),
        ("orange", "Orange", "#FF8A00"),
        ("yellow", "Yellow", "#FFCC00"),
        ("green", "Green", "#30C25A"),
        ("blue", "Blue", "#0A84FF"),
        ("purple", "Purple", "#8754F2"),
        ("white", "White", "#FFFFFF"),
        ("black", "Black", "#1D1D21"),
    };

    public static readonly (string Id, string Title)[] TapSounds =
    {
        ("thock", "Thock"), ("click", "Click"), ("pop", "Pop"),
        ("wood", "Wood block"), ("boop", "Boop"), ("squeak", "Squeak"),
    };

    public static readonly (string Id, string Title)[] Shapes =
    {
        ("pen", "Pen"), ("ellipse", "Ellipse"), ("rectangle", "Rectangle"), ("arrow", "Arrow"),
    };

    public static readonly (string Id, string Title)[] ClickEffects =
    {
        ("glow", "Glow"), ("sparkle", "Sparkle burst"), ("off", "Off"),
    };

    public static readonly (string Id, string Title)[] InteractKeys =
    {
        ("alt", "Alt"), ("ctrl", "Ctrl"), ("win", "Windows key"), ("off", "Off"),
    };

    public static readonly IReadOnlyList<Hand> Hands = LoadHands();

    /// <summary>Every style id in menu order: the hands, then the laser dot.</summary>
    public static IEnumerable<(string Id, string Title)> Styles =>
        Hands.Select(h => (h.Id, h.Title)).Append((LaserId, LaserTitle));

    public static Hand? FindHand(string id) => Hands.FirstOrDefault(h => h.Id == id);

    public static string TitleOf((string Id, string Title)[] list, string id) =>
        list.FirstOrDefault(e => e.Id == id).Title ?? list[0].Title;

    private static List<Hand> LoadHands()
    {
        using var stream = Assets.Open("assets/hands.json");
        return JsonSerializer.Deserialize<List<Hand>>(stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<Hand>();
    }

    public static Color ParseHex(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        return Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
    }
}

/// <summary>Images, sounds and data built into the exe.</summary>
internal static class Assets
{
    private static readonly Assembly Self = typeof(Assets).Assembly;

    // Resource names use the build machine's path separator; accept either.
    private static readonly Dictionary<string, string> Names =
        Self.GetManifestResourceNames().ToDictionary(n => n.Replace('\\', '/'), n => n);

    public static Stream Open(string path) =>
        Self.GetManifestResourceStream(Names[path]) ?? throw new FileNotFoundException(path);

    public static Bitmap Image(string path)
    {
        using var stream = Open(path);
        using var loaded = new Bitmap(stream);
        // Copy so the stream can close; keep straight alpha for scaling.
        var copy = new Bitmap(loaded.Width, loaded.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(copy))
            g.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
        return copy;
    }

    public static Icon AppIcon()
    {
        using var stream = Open("TinyHand.ico");
        return new Icon(stream);
    }
}
