using ComBridge;

namespace ComBridge.Tests;

public sealed class CoordinateMapperTests
{
    private static readonly VirtualScreen Screen = new(-1920, -200, 4480, 1640);

    [Theory]
    [InlineData(0, 0, -1920, -200)]
    [InlineData(4479, 1639, 2559, 1439)]
    public void ToDesktopMapsImagePixels(int x, int y, int expected_x, int expected_y)
    {
        Assert.Equal(new DesktopPoint(expected_x, expected_y), CoordinateMapper.ToDesktop(x, y, Screen));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(4480, 0)]
    [InlineData(0, 1640)]
    public void ToDesktopRejectsPixelsOutsideImage(int x, int y)
    {
        Assert.Throws<RequestValidationException>(() => CoordinateMapper.ToDesktop(x, y, Screen));
    }
}

