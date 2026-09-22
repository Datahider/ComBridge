namespace ComBridge;

public sealed record ScreenRequest(int? Monitor = null, int? X = null, int? Y = null,
    int? Width = null, int? Height = null, string Format = "png", int Quality = 80);
public sealed record CaptureArea(int ImageLeft, int ImageTop, int DesktopLeft, int DesktopTop, int Width, int Height);
public sealed record ImageOutputFormat(string Name, string ContentType, int Quality);

public static class ScreenRequestResolver
{
    public static CaptureArea Resolve(ScreenInfo info, ScreenRequest request)
    {
        var region_values = new[] { request.X, request.Y, request.Width, request.Height };
        var has_region = region_values.Any(value => value.HasValue);
        if (request.Monitor.HasValue && has_region)
            throw new RequestValidationException("monitor cannot be combined with x, y, width, or height.");
        if (has_region && region_values.Any(value => !value.HasValue))
            throw new RequestValidationException("x, y, width, and height must be supplied together.");

        if (request.Monitor.HasValue)
        {
            var monitor = info.Monitors.SingleOrDefault(item => item.Id == request.Monitor.Value)
                ?? throw new RequestValidationException($"Monitor {request.Monitor.Value} does not exist.");
            return new CaptureArea(monitor.Left - info.VirtualScreen.Left, monitor.Top - info.VirtualScreen.Top,
                monitor.Left, monitor.Top, monitor.Width, monitor.Height);
        }

        if (has_region)
        {
            var x = request.X!.Value; var y = request.Y!.Value;
            var width = request.Width!.Value; var height = request.Height!.Value;
            if (x < 0 || y < 0 || width <= 0 || height <= 0 ||
                (long)x + width > info.VirtualScreen.Width || (long)y + height > info.VirtualScreen.Height)
                throw new RequestValidationException("The requested screen region is outside the virtual screen image.");
            return new CaptureArea(x, y, info.VirtualScreen.Left + x, info.VirtualScreen.Top + y, width, height);
        }

        return new CaptureArea(0, 0, info.VirtualScreen.Left, info.VirtualScreen.Top,
            info.VirtualScreen.Width, info.VirtualScreen.Height);
    }

    public static ImageOutputFormat ResolveFormat(string? format, int quality)
    {
        var normalized = (format ?? "png").Trim().ToLowerInvariant();
        if (normalized == "png") return new ImageOutputFormat("png", "image/png", quality);
        if (normalized is not ("jpeg" or "jpg"))
            throw new RequestValidationException("format must be png or jpeg.");
        if (quality is < 1 or > 100)
            throw new RequestValidationException("quality must be between 1 and 100.");
        return new ImageOutputFormat("jpeg", "image/jpeg", quality);
    }
}

