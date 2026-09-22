using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace ComBridge;

public sealed record RectangleInfo(int Left, int Top, int Width, int Height);
public sealed record WindowInfo(string Id, string Title, uint ProcessId, RectangleInfo DesktopBounds,
    RectangleInfo ImageBounds, bool Minimized, bool Foreground);
public sealed record ActivateWindowRequest(string Id);
public sealed record ActivateWindowResponse(bool Success, string Id, string Title);
public sealed class WindowActivationException(string message) : Exception(message);

public static class WindowId
{
    public static nint Parse(string value)
    {
        var normalized = value?.Trim() ?? "";
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        if (!ulong.TryParse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed) || parsed == 0)
            throw new RequestValidationException("id must be a non-zero hexadecimal window id.");
        try { return checked((nint)parsed); }
        catch (OverflowException) { throw new RequestValidationException("id is outside the native pointer range."); }
    }

    public static string Format(nint window) => $"0x{window.ToInt64():X}";
}

public static class WindowCoordinateMapper
{
    public static RectangleInfo ToImageBounds(RectangleInfo bounds, VirtualScreen screen) =>
        new(bounds.Left - screen.Left, bounds.Top - screen.Top, bounds.Width, bounds.Height);
}

public interface IWindowController
{
    IReadOnlyList<WindowInfo> List(VirtualScreen screen);
    ActivateWindowResponse Activate(string id);
}

public sealed class WindowController : IWindowController
{
    public IReadOnlyList<WindowInfo> List(VirtualScreen screen)
    {
        DesktopService.EnsureWindows();
        var foreground = NativeMethods.GetForegroundWindow();
        var windows = new List<WindowInfo>();
        NativeMethods.EnumWindowsProc callback = (window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window)) return true;
            var title_length = NativeMethods.GetWindowTextLength(window);
            if (title_length <= 0) return true;
            var title = new StringBuilder(title_length + 1);
            if (NativeMethods.GetWindowText(window, title, title.Capacity) == 0) return true;
            if (!NativeMethods.GetWindowRect(window, out var rect))
                throw new WindowsApiException("GetWindowRect", Marshal.GetLastWin32Error());
            var width = rect.Right - rect.Left; var height = rect.Bottom - rect.Top;
            if (width <= 0 || height <= 0) return true;
            NativeMethods.GetWindowThreadProcessId(window, out var process_id);
            var desktop_bounds = new RectangleInfo(rect.Left, rect.Top, width, height);
            windows.Add(new WindowInfo(WindowId.Format(window), title.ToString(), process_id, desktop_bounds,
                WindowCoordinateMapper.ToImageBounds(desktop_bounds, screen), NativeMethods.IsIconic(window), window == foreground));
            return true;
        };
        if (!NativeMethods.EnumWindows(callback, nint.Zero))
            throw new WindowsApiException("EnumWindows", Marshal.GetLastWin32Error());
        return windows;
    }

    public ActivateWindowResponse Activate(string id)
    {
        DesktopService.EnsureWindows();
        var window = WindowId.Parse(id);
        if (!NativeMethods.IsWindow(window)) throw new RequestValidationException("The window id no longer exists.");
        if (NativeMethods.IsIconic(window) && !NativeMethods.ShowWindowAsync(window, NativeMethods.SW_RESTORE))
            throw new WindowActivationException("ShowWindowAsync could not restore the window.");
        if (!NativeMethods.SetForegroundWindow(window))
            throw new WindowActivationException("Windows refused to place the requested window in the foreground.");
        var length = NativeMethods.GetWindowTextLength(window);
        var title = new StringBuilder(Math.Max(1, length + 1));
        NativeMethods.GetWindowText(window, title, title.Capacity);
        return new ActivateWindowResponse(true, WindowId.Format(window), title.ToString());
    }
}

