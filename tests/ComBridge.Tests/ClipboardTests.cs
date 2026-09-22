using ComBridge;

namespace ComBridge.Tests;

public sealed class ClipboardTests
{
    [Fact]
    public void ClipboardResponseReportsUtf16Length()
    {
        Assert.Equal(new ClipboardTextResponse("A😀Ж", 4), ClipboardTextResponse.Create("A😀Ж"));
    }

    [Fact]
    public void EmptyClipboardTextIsValid()
    {
        RequestValidator.Validate(new ClipboardTextRequest(""));
    }

    [Fact]
    public void NullClipboardTextIsRejected()
    {
        Assert.Throws<RequestValidationException>(() =>
            RequestValidator.Validate(new ClipboardTextRequest(null!)));
    }

    [Fact]
    public void OversizedClipboardTextIsRejected()
    {
        Assert.Throws<RequestValidationException>(() =>
            RequestValidator.Validate(new ClipboardTextRequest(new string('x', 10_000_001))));
    }

    [Fact]
    public void MissingTextFormatHasStableErrorCode()
    {
        Assert.Equal("clipboard_text_unavailable", ApiErrors.ClipboardTextUnavailable("missing").Error);
    }
}

