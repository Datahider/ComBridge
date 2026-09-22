using System.Runtime.InteropServices;

namespace ComBridge;

public interface IMouseController
{
    MousePosition GetPosition(ScreenInfo info);
    void Move(int x, int y, ScreenInfo info);
    void Click(ClickRequest request, ScreenInfo info);
    void Scroll(ScrollRequest request, ScreenInfo info);
    Task DragAsync(DragRequest request, ScreenInfo info, CancellationToken cancellation_token);
}

public sealed class MouseController : IMouseController
{
    public MousePosition GetPosition(ScreenInfo info)
    {
        DesktopService.EnsureWindows();
        if (!NativeMethods.GetCursorPos(out var point))
            throw new WindowsApiException("GetCursorPos", Marshal.GetLastWin32Error());
        return new MousePosition(point.X - info.VirtualScreen.Left, point.Y - info.VirtualScreen.Top, point.X, point.Y);
    }
    public void Move(int x, int y, ScreenInfo info) => Send([MoveInput(x, y, info)]);

    public void Click(ClickRequest request, ScreenInfo info)
    {
        var (down, up) = request.Button.ToLowerInvariant() switch
        {
            "left" => (NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP),
            "right" => (NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP),
            "middle" => (NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP),
            _ => throw new RequestValidationException("button must be left, right, or middle.")
        };
        var inputs = new List<NativeMethods.INPUT> { MoveInput(request.X, request.Y, info) };
        for (var index = 0; index < request.Count; index++)
        {
            inputs.Add(MouseButton(down));
            inputs.Add(MouseButton(up));
        }
        Send(inputs.ToArray());
    }

    public void Scroll(ScrollRequest request, ScreenInfo info)
    {
        var inputs = new List<NativeMethods.INPUT>();
        if (request.X.HasValue) inputs.Add(MoveInput(request.X.Value, request.Y!.Value, info));
        inputs.Add(new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion
        { mi = new NativeMethods.MOUSEINPUT { mouseData = unchecked((uint)request.Delta), dwFlags = NativeMethods.MOUSEEVENTF_WHEEL } } });
        Send(inputs.ToArray());
    }

    public async Task DragAsync(DragRequest request, ScreenInfo info, CancellationToken cancellation_token)
    {
        Move(request.FromX, request.FromY, info);
        Send([MouseButton(NativeMethods.MOUSEEVENTF_LEFTDOWN)]);
        try
        {
            const int interval_ms = 10;
            var steps = Math.Max(1, request.DurationMs / interval_ms);
            for (var step = 1; step <= steps; step++)
            {
                var x = request.FromX + (request.ToX - request.FromX) * step / steps;
                var y = request.FromY + (request.ToY - request.FromY) * step / steps;
                Move(x, y, info);
                if (request.DurationMs > 0) await Task.Delay(request.DurationMs / steps, cancellation_token);
            }
        }
        finally
        {
            Send([MouseButton(NativeMethods.MOUSEEVENTF_LEFTUP)]);
        }
    }

    private static NativeMethods.INPUT MoveInput(int x, int y, ScreenInfo info)
    {
        var point = CoordinateMapper.ToDesktop(x, y, info.VirtualScreen);
        var width = info.VirtualScreen.Width;
        var height = info.VirtualScreen.Height;
        var normalized_x = width == 1 ? 0 : (int)Math.Round((point.X - info.VirtualScreen.Left) * 65535.0 / (width - 1));
        var normalized_y = height == 1 ? 0 : (int)Math.Round((point.Y - info.VirtualScreen.Top) * 65535.0 / (height - 1));
        return new NativeMethods.INPUT { type = NativeMethods.INPUT_MOUSE, U = new NativeMethods.InputUnion
        { mi = new NativeMethods.MOUSEINPUT { dx = normalized_x, dy = normalized_y,
            dwFlags = NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK } } };
    }

    private static NativeMethods.INPUT MouseButton(uint flag) => new() { type = NativeMethods.INPUT_MOUSE,
        U = new NativeMethods.InputUnion { mi = new NativeMethods.MOUSEINPUT { dwFlags = flag } } };

    private static void Send(NativeMethods.INPUT[] inputs)
    {
        DesktopService.EnsureWindows();
        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        if (sent != inputs.Length) throw new WindowsApiException("SendInput(mouse)", Marshal.GetLastWin32Error());
    }
}

public sealed record MousePosition(int X, int Y, int DesktopX, int DesktopY);
