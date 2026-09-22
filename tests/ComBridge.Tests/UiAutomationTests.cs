using ComBridge;

namespace ComBridge.Tests;

public sealed class UiAutomationTests
{
    private static readonly UiElementSnapshot Button = new("Save", "saveButton", "Button", "ButtonClass");

    [Fact]
    public void SelectorMatchesAllSpecifiedFieldsIgnoringCase()
    {
        var selector = new UiSelector("save", "SAVEBUTTON", "button", "buttonclass");
        Assert.True(selector.Matches(Button));
    }

    [Fact]
    public void SelectorRejectsPartialMatches()
    {
        Assert.False(new UiSelector(Name: "Save as").Matches(Button));
    }

    [Fact]
    public void EmptySelectorIsRejected()
    {
        Assert.Throws<RequestValidationException>(() => UiRequestValidator.Validate(new UiSelector()));
    }

    [Theory]
    [InlineData(-1, 500)]
    [InlineData(11, 500)]
    [InlineData(3, 0)]
    [InlineData(3, 2001)]
    public void InvalidTreeLimitsAreRejected(int depth, int max_nodes)
    {
        Assert.Throws<RequestValidationException>(() => UiRequestValidator.ValidateTree(depth, max_nodes));
    }

    [Theory]
    [InlineData("focus")]
    [InlineData("INVOKE")]
    [InlineData("select")]
    [InlineData("expand")]
    [InlineData("collapse")]
    [InlineData("setValue")]
    public void SupportedActionsAreAccepted(string action)
    {
        UiRequestValidator.Validate(new UiActionRequest("0x1", new UiSelector(Name: "x"), action,
            action.Equals("setValue", StringComparison.OrdinalIgnoreCase) ? "secret" : null));
    }

    [Fact]
    public void SetValueRequiresValue()
    {
        Assert.Throws<RequestValidationException>(() =>
            UiRequestValidator.Validate(new UiActionRequest("0x1", new UiSelector(Name: "x"), "setValue")));
    }

    [Fact]
    public void UnsupportedActionIsRejected()
    {
        Assert.Throws<RequestValidationException>(() =>
            UiRequestValidator.Validate(new UiActionRequest("0x1", new UiSelector(Name: "x"), "click")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public void InvalidFindLimitIsRejected(int max_results)
    {
        Assert.Throws<RequestValidationException>(() => UiRequestValidator.Validate(
            new UiFindRequest("0x1", new UiSelector(Name: "x"), max_results)));
    }
}

