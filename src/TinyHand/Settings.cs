using System.Text.Json;
using Microsoft.Win32;

namespace TinyHand;

/// <summary>Everything the user can change. Saved as JSON in %APPDATA%\TinyHand.</summary>
internal sealed class SettingsData
{
    public string PointerStyle { get; set; } = "lavender";
    public List<string> PinnedStyles { get; set; } = new() { "lavender", "glove", "coral", "plush", "robot", "laser" };
    public double PointerSize { get; set; } = 0.13;
    public bool ShowShadow { get; set; } = true;
    public double FadeSeconds { get; set; } = 3;
    public string InkChoice { get; set; } = "match";
    public string DrawShape { get; set; } = "ellipse";
    public string DoubleClickEffect { get; set; } = "glow";
    public string TapSound { get; set; } = "thock";
    public bool HandSound { get; set; } = true;
    public bool LaserSound { get; set; } = false;
    public string InteractKey { get; set; } = "alt";
    public bool HintShown { get; set; } = false;
}

internal sealed class Settings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;

    /// <summary>The marker file that puts TinyHand in portable mode when it sits next to the exe.</summary>
    public const string PortableMarker = "TinyHand.portable";

    /// <summary>
    /// True when <see cref="PortableMarker"/> is next to the exe. Settings are
    /// then kept in that folder (for example on a USB stick) instead of %APPDATA%.
    /// </summary>
    public static bool Portable { get; } = ExeDir is { } dir && File.Exists(Path.Combine(dir, PortableMarker));

    private static string? ExeDir => Path.GetDirectoryName(Environment.ProcessPath);

    public Settings()
    {
        var dir = Portable
            ? ExeDir!
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TinyHand");
        _path = Path.Combine(dir, "settings.json");
        try
        {
            if (File.Exists(_path))
                Data = JsonSerializer.Deserialize<SettingsData>(File.ReadAllText(_path)) ?? new SettingsData();
        }
        catch (Exception)
        {
            Data = new SettingsData();
        }
        Data.PointerSize = Math.Clamp(Data.PointerSize, 0.08, 0.20);
        Data.FadeSeconds = Math.Clamp(Data.FadeSeconds, 0, 60);
    }

    public SettingsData Data { get; } = new();

    /// <summary>Raised after any change is saved.</summary>
    public event Action? Changed;

    public void Update(Action<SettingsData> change)
    {
        change(Data);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Data, Json));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Changed?.Invoke();
    }

    // --- derived values ------------------------------------------------

    public string Style =>
        Data.PointerStyle == Catalog.LaserId || Catalog.FindHand(Data.PointerStyle) != null ? Data.PointerStyle : "lavender";

    public bool IsLaser => Style == Catalog.LaserId;

    public bool SoundOn => IsLaser ? Data.LaserSound : Data.HandSound;

    public void SetSoundOn(bool on) => Update(d =>
    {
        if (IsLaser) d.LaserSound = on; else d.HandSound = on;
    });

    public string Shape => Catalog.Shapes.Any(s => s.Id == Data.DrawShape) ? Data.DrawShape : "pen";

    public string ClickEffect => Catalog.ClickEffects.Any(s => s.Id == Data.DoubleClickEffect) ? Data.DoubleClickEffect : "glow";

    public Color InkColor
    {
        get
        {
            var choice = Catalog.InkChoices.FirstOrDefault(c => c.Id == Data.InkChoice);
            if (choice.Color != null)
                return Catalog.ParseHex(choice.Color);
            return Catalog.ParseHex(IsLaser ? Catalog.Red : Catalog.FindHand(Style)!.Ink);
        }
    }
}

/// <summary>Starting TinyHand when you sign in, through the usual Run key.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "TinyHand";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) != null;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value && Environment.ProcessPath is { } exe)
                key.SetValue(Name, $"\"{exe}\"");
            else
                key.DeleteValue(Name, false);
        }
    }
}
