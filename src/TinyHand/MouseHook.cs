using System.Runtime.InteropServices;
using static TinyHand.Native;

namespace TinyHand;

internal enum MouseAction { Down, Up }

internal readonly record struct MouseEvent(MouseAction Action, MouseButtons Button, int X, int Y, uint Time);

/// <summary>
/// A low-level mouse hook that swallows button presses while the pointer is
/// on, so taps and drawing never click the app underneath. Movement and the
/// scroll wheel always pass through. It runs on its own thread so a busy
/// frame never delays the system's mouse input.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    private readonly Action<MouseEvent> _onEvent;
    private readonly SynchronizationContext _ui;
    private readonly LowLevelMouseProc _proc;
    private readonly Thread _thread;
    private uint _threadId;
    private IntPtr _hook;
    // Buttons whose press was swallowed; their release must be swallowed too.
    private readonly bool[] _swallowed = new bool[5];

    private volatile bool _active;
    private volatile string _interactKey = "alt";

    public MouseHook(Action<MouseEvent> onEvent)
    {
        _onEvent = onEvent;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create the hook on the UI thread.");
        _proc = Proc;
        var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "TinyHand mouse hook", Priority = ThreadPriority.Highest };
        _thread.Start();
        ready.Wait();
    }

    public bool Active { get => _active; set => _active = value; }

    /// <summary>"alt", "ctrl", "win" or "off": the key that lets clicks through while held.</summary>
    public string InteractKey { get => _interactKey; set => _interactKey = value; }

    public static bool InteractHeld(string key) => key switch
    {
        "alt" => KeyDown(VK_MENU),
        "ctrl" => KeyDown(VK_CONTROL),
        "win" => KeyDown(VK_LWIN) || KeyDown(VK_RWIN),
        _ => false,
    };

    /// <summary>
    /// True when a click at (x, y) should reach whatever is there: while the
    /// click-through key is held, over the taskbar, and over TinyHand's own
    /// menus and settings window.
    /// </summary>
    public static bool PassesThrough(string interactKey, int x, int y) =>
        InteractHeld(interactKey) || !WorkAreaAt(x, y).Contains(x, y) || OwnWindowAt(x, y);

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
        ready.Set();
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
        }
        UnhookWindowsHookEx(_hook);
    }

    private IntPtr Proc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        var msg = (int)wParam;
        var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        var (button, index, down) = Decode(msg, info.MouseData);
        if (index < 0)
            return CallNextHookEx(_hook, nCode, wParam, lParam);

        if (down)
        {
            if (!_active || PassesThrough(_interactKey, info.Pt.X, info.Pt.Y))
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            _swallowed[index] = true;
            Post(new MouseEvent(MouseAction.Down, button, info.Pt.X, info.Pt.Y, info.Time));
            return 1;
        }
        if (!_swallowed[index])
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        _swallowed[index] = false;
        Post(new MouseEvent(MouseAction.Up, button, info.Pt.X, info.Pt.Y, info.Time));
        return 1;
    }

    private void Post(MouseEvent e) => _ui.Post(_ => _onEvent(e), null);

    private static (MouseButtons, int, bool) Decode(int msg, uint data) => msg switch
    {
        WM_LBUTTONDOWN => (MouseButtons.Left, 0, true),
        WM_LBUTTONUP => (MouseButtons.Left, 0, false),
        WM_RBUTTONDOWN => (MouseButtons.Right, 1, true),
        WM_RBUTTONUP => (MouseButtons.Right, 1, false),
        WM_MBUTTONDOWN => (MouseButtons.Middle, 2, true),
        WM_MBUTTONUP => (MouseButtons.Middle, 2, false),
        WM_XBUTTONDOWN or WM_XBUTTONUP => ((data >> 16) == 2 ? MouseButtons.XButton2 : MouseButtons.XButton1,
            (data >> 16) == 2 ? 4 : 3, msg == WM_XBUTTONDOWN),
        _ => (MouseButtons.None, -1, false),
    };

    public void Dispose()
    {
        _active = false;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(1000);
    }
}
