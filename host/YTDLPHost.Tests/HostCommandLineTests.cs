using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class HostCommandLineTests
{
    [Theory]
    [InlineData("--register-native-host", HostCommand.RegisterNativeHost)]
    [InlineData("--REGISTER-NATIVE-HOST", HostCommand.RegisterNativeHost)]
    [InlineData("--unregister-native-host", HostCommand.UnregisterNativeHost)]
    [InlineData("ytdlp://show", HostCommand.None)]
    [InlineData("--register", HostCommand.None)]
    [InlineData("", HostCommand.None)]
    public void A_single_argument_is_a_command_or_not(string arg, HostCommand expected)
    {
        Assert.Equal(expected, HostCommandLine.Parse(new[] { arg }));
    }

    [Fact]
    public void No_arguments_is_no_command()
    {
        Assert.Equal(HostCommand.None, HostCommandLine.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void The_first_command_wins_and_other_arguments_are_ignored()
    {
        Assert.Equal(HostCommand.UnregisterNativeHost, HostCommandLine.Parse(new[] { "--unregister-native-host", "--register-native-host" }));
        Assert.Equal(HostCommand.RegisterNativeHost, HostCommandLine.Parse(new[] { "ytdlp://x", "--register-native-host" }));
    }
}
