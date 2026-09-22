using System.Runtime.InteropServices;

namespace ComBridge;

public sealed record VirtualScreen(int Left, int Top, int Width, int Height);
public sealed record DesktopPoint(int X, int Y);
public sealed record MonitorInfo(int Id, int Left, int Top, int Width, int Height, bool Primary);
public sealed record ImageOffset(int X, int Y);
public sealed record ScreenInfo(VirtualScreen VirtualScreen, ImageOffset ImageOffset, IReadOnlyList<MonitorInfo> Monitors);
public sealed record HealthResponse(string Status, string Version, uint SessionId, bool DesktopAvailable,
    int ScreenWidth, int ScreenHeight, string? Diagnostic = null);

public static class CoordinateMapper
{
    public static DesktopPoint ToDesktop(int x, int y, VirtualScreen screen)
    {
        if (x < 0 || y < 0 || x >= screen.Width || y >= screen.Height)
            throw new RequestValidationException($"Coordinates ({x},{y}) are outside the {screen.Width}x{screen.Height} screen image.");
        return new DesktopPoint(checked(screen.Left + x), checked(screen.Top + y));
    }
}

public interface IDesktopService
{
    ScreenInfo GetScreenInfo();
    bool IsDesktopAvailable(out string? diagnostic);
    uint GetSessionId();
}

public static class DesktopServiceExtensions
{
    public static ScreenInfo RequireScreenInfo(this IDesktopService desktop)
    {
        if (!desktop.IsDesktopAvailable(out var diagnostic))
            throw new DesktopUnavailableException(diagnostic ?? "The interactive desktop is unavailable.");
        return desktop.GetScreenInfo();
    }
}

public sealed class DesktopService : IDesktopService
{
    public ScreenInfo GetScreenInfo()
    {
        EnsureWindows();
        var screen = new VirtualScreen(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));
        if (screen.Width <= 0 || screen.Height <= 0)
            throw new DesktopUnavailableException("The virtual desktop has no usable dimensions.");

        var monitors = new List<MonitorInfo>();
        var id = 0;
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFO { cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFO>() };
            if (!NativeMethods.GetMonitorInfo(monitor, ref info))
                throw new WindowsApiException("GetMonitorInfo", Marshal.GetLastWin32Error());
            monitors.Add(new MonitorInfo(id++, info.rcMonitor.Left, info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top,
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));
            return true;
        };
        if (!NativeMethods.EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero))
            throw new WindowsApiException("EnumDisplayMonitors", Marshal.GetLastWin32Error());
        return new ScreenInfo(screen, new ImageOffset(screen.Left, screen.Top), monitors);
    }

    public bool IsDesktopAvailable(out string? diagnostic)
    {
        diagnostic = null;
        if (!OperatingSystem.IsWindows())
        {
            diagnostic = "ComBridge desktop access is available only on Windows.";
            return false;
        }
        var desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS | NativeMethods.DESKTOP_WRITEOBJECTS);
        if (desktop == nint.Zero)
        {
            diagnostic = $"OpenInputDesktop failed with Win32 error {Marshal.GetLastWin32Error()}; the session may be locked or non-interactive.";
            return false;
        }
        NativeMethods.CloseDesktop(desktop);
        return true;
    }

    public uint GetSessionId()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        if (!NativeMethods.ProcessIdToSessionId((uint)Environment.ProcessId, out var session_id))
            throw new WindowsApiException("ProcessIdToSessionId", Marshal.GetLastWin32Error());
        return session_id;
    }

    public static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("ComBridge requires Windows.");
    }
}
