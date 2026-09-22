using ComBridge;

namespace ComBridge.Tests;

public sealed class RequestValidatorTests
{
    [Theory]
    [InlineData("left", 1)]
    [InlineData("RIGHT", 2)]
    [InlineData("middle", 1)]
    public void ValidClickIsAccepted(string button, int count)
    {
        RequestValidator.Validate(new ClickRequest(1, 2, button, count));
    }

    [Theory]
    [InlineData("other", 1)]
    [InlineData("left", 0)]
    [InlineData("left", 3)]
    public void InvalidClickIsRejected(string button, int count)
    {
        Assert.Throws<RequestValidationException>(() => RequestValidator.Validate(new ClickRequest(1, 2, button, count)));
    }

    [Fact]
    public void ScrollCoordinatesMustBeBothPresentOrBothAbsent()
    {
        Assert.Throws<RequestValidationException>(() => RequestValidator.Validate(new ScrollRequest(120, 10, null)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(30001)]
    public void InvalidDragDurationIsRejected(int duration_ms)
    {
        Assert.Throws<RequestValidationException>(() => RequestValidator.Validate(new DragRequest(0, 0, 1, 1, duration_ms)));
    }

    [Fact]
    public void EmptyHotkeyIsRejected()
    {
        Assert.Throws<RequestValidationException>(() => RequestValidator.Validate(new HotkeyRequest([])));
    }

    [Fact]
    public void ErrorPayloadRetainsMachineReadableCode()
    {
        Assert.Equal(new ApiError("invalid_request", "bad"), ApiErrors.InvalidRequest("bad"));
    }
}

