using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ComBridge;

public interface IScreenCapture { byte[] CapturePng(ScreenInfo info); }

public sealed class ScreenCapture : IScreenCapture
{
    [SupportedOSPlatform("windows6.1")]
    public byte[] CapturePng(ScreenInfo info)
    {
        DesktopService.EnsureWindows();
        var screen = info.VirtualScreen;
        var desktop = NativeMethods.GetDesktopWindow();
        var source_dc = NativeMethods.GetWindowDC(desktop);
        if (source_dc == nint.Zero) throw LastError("GetWindowDC");
        nint memory_dc = nint.Zero, bitmap_handle = nint.Zero, previous = nint.Zero;
        try
        {
            memory_dc = NativeMethods.CreateCompatibleDC(source_dc);
            if (memory_dc == nint.Zero) throw LastError("CreateCompatibleDC");
            bitmap_handle = NativeMethods.CreateCompatibleBitmap(source_dc, screen.Width, screen.Height);
            if (bitmap_handle == nint.Zero) throw LastError("CreateCompatibleBitmap");
            previous = NativeMethods.SelectObject(memory_dc, bitmap_handle);
            if (previous == nint.Zero) throw LastError("SelectObject");
            if (!NativeMethods.BitBlt(memory_dc, 0, 0, screen.Width, screen.Height, source_dc,
                    screen.Left, screen.Top, NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw LastError("BitBlt");
            using var bitmap = Image.FromHbitmap(bitmap_handle);
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            if (previous != nint.Zero && memory_dc != nint.Zero) NativeMethods.SelectObject(memory_dc, previous);
            if (bitmap_handle != nint.Zero) NativeMethods.DeleteObject(bitmap_handle);
            if (memory_dc != nint.Zero) NativeMethods.DeleteDC(memory_dc);
            NativeMethods.ReleaseDC(desktop, source_dc);
        }
    }

    private static WindowsApiException LastError(string operation) => new(operation, Marshal.GetLastWin32Error());
}
