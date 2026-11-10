using System.Media;
using static TinyHand.Native;

namespace TinyHand;

/// <summary>
/// Hides the mouse pointer everywhere by swapping every system cursor for a
/// blank one, and brings the user's own cursors back afterwards. Windows has
/// no other way to hide the pointer over other apps' windows.
/// </summary>
internal static class SystemCursors
{
    private static readonly uint[] Ids =
    {
        32512, 32513, 32514, 32515, 32516, 32640, 32641, 32642, 32643, 32644,
        32645, 32646, 32648, 32649, 32650, 32651, 32671, 32672,
    };

    public static bool Hidden { get; private set; }

    public static void Hide()
    {
        if (Hidden)
            return;
        Hidden = true;
        var and = Enumerable.Repeat((byte)0xFF, 128).ToArray();
        var xor = new byte[128];
        var module = GetModuleHandle(null);
        foreach (var id in Ids)
            SetSystemCursor(CreateCursor(module, 0, 0, 32, 32, and, xor), id); // Windows owns the cursor afterwards
    }

    /// <summary>Reloads the user's cursor scheme. Safe to call at any time.</summary>
    public static void Show()
    {
        Hidden = false;
        SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
    }
}

/// <summary>The tap sound, built into the exe.</summary>
internal sealed class TapSound : IDisposable
{
    private readonly Dictionary<string, SoundPlayer> _players = new();

    public void Play(string choice)
    {
        if (!_players.TryGetValue(choice, out var player))
        {
            try
            {
                player = new SoundPlayer(Assets.Open($"assets/sounds/tap_{choice}.wav"));
                player.Load();
            }
            catch (Exception)
            {
                return;
            }
            _players[choice] = player;
        }
        try
        {
            player.Play();
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        foreach (var p in _players.Values)
        {
            p.Stream?.Dispose();
            p.Dispose();
        }
    }
}
