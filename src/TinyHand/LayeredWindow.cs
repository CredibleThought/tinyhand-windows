using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using static TinyHand.Native;

namespace TinyHand;

/// <summary>
/// A borderless, always-on-top window that shows a per-pixel-alpha image and
/// never takes the mouse or keyboard focus: clicks go straight through it.
/// Draw into the Graphics from <see cref="Begin"/>, then call <see cref="Commit"/>.
/// </summary>
internal sealed class LayeredWindow : NativeWindow, IDisposable
{
    private IntPtr _dc, _dib, _oldBitmap, _bits;
    private Bitmap? _bitmap;
    private Graphics? _graphics;
    private int _capW, _capH;
    private Rectangle _bounds;
    private bool _shown;

    public LayeredWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = "TinyHand",
            Style = WS_POPUP,
            ExStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            Width = 1,
            Height = 1,
        });
    }

    public Rectangle Bounds => _bounds;
    public bool Shown => _shown;

    /// <summary>Starts a new frame covering <paramref name="bounds"/> (screen pixels), cleared to transparent.</summary>
    public Graphics Begin(Rectangle bounds)
    {
        _bounds = bounds;
        Ensure(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        var g = _graphics!;
        g.Flush(FlushIntention.Sync);
        g.ResetTransform();
        g.ResetClip();
        // Clearing the memory directly is much faster than filling with GDI+.
        unsafe
        {
            var rowBytes = (nuint)(Math.Max(1, bounds.Width) * 4);
            for (var y = 0; y < Math.Max(1, bounds.Height); y++)
                System.Runtime.InteropServices.NativeMemory.Clear((byte*)_bits + (long)y * _capW * 4, rowBytes);
        }
        g.SetClip(new Rectangle(0, 0, bounds.Width, bounds.Height));
        return g;
    }

    /// <summary>Puts the last frame on screen, faded by <paramref name="alpha"/>.</summary>
    public void Commit(byte alpha = 255)
    {
        if (_dc == IntPtr.Zero || _bounds.Width <= 0 || _bounds.Height <= 0)
            return;
        _graphics!.Flush(FlushIntention.Sync);
        var dst = new POINT(_bounds.X, _bounds.Y);
        var size = new SIZE(_bounds.Width, _bounds.Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, _dc, ref src, 0, ref blend, ULW_ALPHA);
        if (!_shown)
        {
            ShowWindow(Handle, SW_SHOWNOACTIVATE);
            _shown = true;
        }
    }

    public void Hide()
    {
        if (!_shown)
            return;
        ShowWindow(Handle, SW_HIDE);
        _shown = false;
    }

    /// <summary>Moves the window to the top of the always-on-top windows.</summary>
    public void BringToTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    // A DIB section the window copies from; it only grows, so a window that
    // changes size every frame (the hand) does not reallocate every frame.
    private void Ensure(int w, int h)
    {
        if (w <= _capW && h <= _capH)
            return;
        FreeSurface();
        _capW = Math.Max(w, _capW) + 64;
        _capH = Math.Max(h, _capH) + 64;
        var header = new BITMAPINFOHEADER
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<BITMAPINFOHEADER>(),
            Width = _capW,
            Height = -_capH, // top-down rows
            Planes = 1,
            BitCount = 32,
        };
        var screen = GetDC(IntPtr.Zero);
        _dc = CreateCompatibleDC(screen);
        ReleaseDC(IntPtr.Zero, screen);
        _dib = CreateDIBSection(_dc, ref header, 0, out _bits, IntPtr.Zero, 0);
        _oldBitmap = SelectObject(_dc, _dib);
        _bitmap = new Bitmap(_capW, _capH, _capW * 4, PixelFormat.Format32bppPArgb, _bits);
        _graphics = Graphics.FromImage(_bitmap);
        _graphics.SmoothingMode = SmoothingMode.AntiAlias;
        // Images are scaled once beforehand, so plain bilinear sampling is enough.
        _graphics.InterpolationMode = InterpolationMode.Bilinear;
        _graphics.PixelOffsetMode = PixelOffsetMode.Half;
        _graphics.CompositingQuality = CompositingQuality.HighSpeed;
    }

    private void FreeSurface()
    {
        _graphics?.Dispose();
        _bitmap?.Dispose();
        _graphics = null;
        _bitmap = null;
        if (_dc != IntPtr.Zero)
        {
            SelectObject(_dc, _oldBitmap);
            DeleteObject(_dib);
            DeleteDC(_dc);
            _dc = _dib = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        FreeSurface();
        DestroyHandle();
    }
}
