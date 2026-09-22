using ComBridge;

namespace ComBridge.Tests;

public sealed class KeyboardTests
{
    [Fact]
    public void UnicodeInputUsesEveryUtf16CodeUnitIncludingSurrogates()
    {
        var inputs = InputFactory.UnicodeText("AЖ😀");

        Assert.Equal(8, inputs.Count);
        Assert.Equal(new ushort[] { 0x0041, 0x0416, 0xD83D, 0xDE00 },
            inputs.Where((_, index) => index % 2 == 0).Select(input => input.ScanCode));
        Assert.All(inputs.Where((_, index) => index % 2 == 0), input => Assert.False(input.KeyUp));
        Assert.All(inputs.Where((_, index) => index % 2 == 1), input => Assert.True(input.KeyUp));
    }

    [Fact]
    public void HotkeyReleasesKeysInReverseOrder()
    {
        var inputs = InputFactory.Hotkey(["CTRL", "SHIFT", "S"]);

        Assert.Equal(new ushort[] { 0x11, 0x10, 0x53, 0x53, 0x10, 0x11 }, inputs.Select(input => input.VirtualKey));
        Assert.Equal(new[] { false, false, false, true, true, true }, inputs.Select(input => input.KeyUp));
    }

    [Theory]
    [InlineData("F13")]
    [InlineData("CTRLISH")]
    [InlineData("")]
    public void HotkeyRejectsUnknownKeys(string key)
    {
        Assert.Throws<RequestValidationException>(() => InputFactory.Hotkey([key]));
    }
}

