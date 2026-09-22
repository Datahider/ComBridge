using ComBridge;

namespace ComBridge.Tests;

public sealed class ScreenRequestTests
{
    private static readonly ScreenInfo Info = new(
        new VirtualScreen(-1920, 0, 3840, 1080), new ImageOffset(-1920, 0),
        [new MonitorInfo(0, -1920, 0, 1920, 1080, false), new MonitorInfo(1, 0, 0, 1920, 1080, true)]);

    [Fact]
    public void FullScreenUsesEntireImage()
    {
        Assert.Equal(new CaptureArea(0, 0, -1920, 0, 3840, 1080),
            ScreenRequestResolver.Resolve(Info, new ScreenRequest()));
    }

    [Fact]
    public void LeftMonitorMapsNegativeDesktopCoordinates()
    {
        Assert.Equal(new CaptureArea(0, 0, -1920, 0, 1920, 1080),
            ScreenRequestResolver.Resolve(Info, new ScreenRequest(Monitor: 0)));
    }

    [Fact]
    public void PrimaryMonitorStartsAtImageX1920()
    {
        Assert.Equal(new CaptureArea(1920, 0, 0, 0, 1920, 1080),
            ScreenRequestResolver.Resolve(Info, new ScreenRequest(Monitor: 1)));
    }

    [Fact]
    public void RegionUsesImageCoordinates()
    {
        Assert.Equal(new CaptureArea(2000, 10, 80, 10, 300, 200),
            ScreenRequestResolver.Resolve(Info, new ScreenRequest(X: 2000, Y: 10, Width: 300, Height: 200)));
    }

    [Theory]
    [InlineData(0, null, null, null, 1)]
    [InlineData(null, 0, null, 100, 100)]
    [InlineData(null, 3800, 0, 100, 100)]
    public void InvalidSelectionsAreRejected(int? monitor, int? x, int? y, int? width, int? height = null)
    {
        Assert.Throws<RequestValidationException>(() => ScreenRequestResolver.Resolve(Info,
            new ScreenRequest(monitor, x, y, width, height)));
    }

    [Theory]
    [InlineData("png", 80, "image/png")]
    [InlineData("JPEG", 1, "image/jpeg")]
    [InlineData("jpg", 100, "image/jpeg")]
    public void ImageFormatIsValidated(string format, int quality, string content_type)
    {
        Assert.Equal(content_type, ScreenRequestResolver.ResolveFormat(format, quality).ContentType);
    }

    [Theory]
    [InlineData("webp", 80)]
    [InlineData("jpeg", 0)]
    [InlineData("jpeg", 101)]
    public void InvalidImageFormatIsRejected(string format, int quality)
    {
        Assert.Throws<RequestValidationException>(() => ScreenRequestResolver.ResolveFormat(format, quality));
    }
}

