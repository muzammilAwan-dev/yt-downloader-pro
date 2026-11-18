using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 4.5. The reply samples are real output of dbus-send and gdbus from a session bus (NameHasOwner for a name that exists and one that does not).</summary>
public class TrayPolicyTests
{
    private const string DbusSendTrue =
        "method return time=1794975978.793313 sender=org.freedesktop.DBus -> destination=:1.0 serial=3 reply_serial=2\n   boolean true\n";
    private const string DbusSendFalse =
        "method return time=1794975978.794940 sender=org.freedesktop.DBus -> destination=:1.1 serial=3 reply_serial=2\n   boolean false\n";

    [Fact]
    public void A_dbus_send_reply_with_true_means_the_watcher_exists() => Assert.True(TrayPolicy.ParseNameHasOwner(DbusSendTrue));

    [Fact]
    public void A_dbus_send_reply_with_false_means_it_does_not() => Assert.False(TrayPolicy.ParseNameHasOwner(DbusSendFalse));

    [Theory]
    [InlineData("(true,)\n", true)]
    [InlineData("(false,)\n", false)]
    public void A_gdbus_reply_is_understood(string output, bool expected) => Assert.Equal(expected, TrayPolicy.ParseNameHasOwner(output));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Failed to open connection to \"session\" message bus: Unable to autolaunch a dbus-daemon without a $DISPLAY for X11")]
    public void Anything_else_is_unknown(string? output) => Assert.Null(TrayPolicy.ParseNameHasOwner(output));

    [Theory]
    [InlineData(HostOs.Windows, null, true)]
    [InlineData(HostOs.Windows, false, true)]
    [InlineData(HostOs.MacOS, null, true)]
    [InlineData(HostOs.Linux, true, true)]
    [InlineData(HostOs.Linux, false, false)]
    [InlineData(HostOs.Linux, null, false)]
    public void Only_Linux_without_a_watcher_cannot_hide_to_the_tray(HostOs os, bool? watcher, bool expected) =>
        Assert.Equal(expected, TrayPolicy.CanHideToTray(os, watcher));

    [Theory]
    [InlineData("wayland", null, true)]
    [InlineData("Wayland", null, true)]
    [InlineData("x11", null, false)]
    [InlineData(null, "wayland-0", true)]
    [InlineData(null, null, false)]
    public void Wayland_is_recognised_from_the_session_type_or_the_display(string? sessionType, string? waylandDisplay, bool expected)
    {
        string? Env(string name) => name == "XDG_SESSION_TYPE" ? sessionType : name == "WAYLAND_DISPLAY" ? waylandDisplay : null;
        Assert.Equal(expected, TrayPolicy.IsWayland(Env));
    }
}
