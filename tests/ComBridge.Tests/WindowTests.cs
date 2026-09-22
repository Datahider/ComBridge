using ComBridge;

namespace ComBridge.Tests;

public sealed class WindowTests
{
    [Theory]
    [InlineData("0x123ABC", 0x123ABC)]
    [InlineData("123abc", 0x123ABC)]
    public void WindowIdParsesHex(string value, long expected)
    {
        Assert.Equal(new nint(expected), WindowId.Parse(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0x0")]
    [InlineData("hello")]
    [InlineData("-1")]
    public void InvalidWindowIdIsRejected(string value)
    {
        Assert.Throws<RequestValidationException>(() => WindowId.Parse(value));
    }

    [Fact]
    public void DesktopBoundsConvertToImageBounds()
    {
        var screen = new VirtualScreen(-1920, -200, 4480, 1640);
        Assert.Equal(new RectangleInfo(2020, 300, 400, 500),
            WindowCoordinateMapper.ToImageBounds(new RectangleInfo(100, 100, 400, 500), screen));
    }
}

