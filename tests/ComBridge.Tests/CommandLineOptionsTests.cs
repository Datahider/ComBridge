using ComBridge;

namespace ComBridge.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void DefaultsAreLoopbackAnd8088()
    {
        Assert.Equal(new CommandLineOptions("127.0.0.1", 8088), CommandLineOptions.Parse([]));
    }

    [Fact]
    public void CustomLoopbackAndPortAreAccepted()
    {
        Assert.Equal(new CommandLineOptions("::1", 18088),
            CommandLineOptions.Parse(["--address", "::1", "--port", "18088"]));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    public void NonLoopbackAddressIsRejected(string address)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--address", address]));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-number")]
    public void InvalidPortIsRejected(string port)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--port", port]));
    }

    [Theory]
    [InlineData("--port")]
    [InlineData("--address")]
    public void MissingOptionValueIsRejected(string option)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse([option]));
    }
}
