using System.Text;
using Xunit;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>Step 4.2: desktop notifications through the OS's own tool. The commands are plain data and the runner is a fake, so this runs on every OS.</summary>
public class NotificationTests
{
    private static string Line(ShellCommand c) => c.FileName + " " + string.Join(" | ", c.Arguments);

    [Fact]
    public void On_Linux_notify_send_gets_the_app_name_and_the_text_after_a_double_dash()
    {
        var commands = NotificationCommands.Build(HostOs.Linux, "-Download", "-a.mp4 done");

        Assert.Equal(new[] { "notify-send --app-name=YT Downloader Pro | -- | -Download | -a.mp4 done" }, commands.Select(Line));
    }

    [Fact]
    public void On_macOS_quotes_and_backslashes_cannot_break_out_of_the_AppleScript_text()
    {
        var commands = NotificationCommands.Build(HostOs.MacOS, "Done", "He said \"hi\" \\ and \"; do shell script \"x");

        var command = Assert.Single(commands);
        Assert.Equal("osascript", command.FileName);
        Assert.Equal("-e", command.Arguments[0]);
        Assert.Equal("display notification \"He said \\\"hi\\\" \\\\ and \\\"; do shell script \\\"x\" with title \"Done\"", command.Arguments[1]);
    }

    [Fact]
    public void On_Windows_the_toast_script_is_passed_encoded_and_its_text_is_xml_and_quote_safe()
    {
        var commands = NotificationCommands.Build(HostOs.Windows, "Done", "<b>O'Neil & \"co\"</b>.mp4");

        var command = Assert.Single(commands);
        Assert.Equal("powershell.exe", command.FileName);
        Assert.Contains("-EncodedCommand", command.Arguments);
        string script = Encoding.Unicode.GetString(Convert.FromBase64String(command.Arguments[^1]));

        Assert.Contains("&lt;b&gt;O&apos;Neil &amp; &quot;co&quot;&lt;/b&gt;.mp4", script);
        Assert.DoesNotContain("<b>", script);
        Assert.Contains("{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}", script);
        Assert.Contains(".Show($toast)", script);
    }

    [Fact]
    public void Line_breaks_are_flattened_and_a_very_long_text_is_cut()
    {
        string body = "first\r\nsecond\tthird " + new string('x', 400);

        var command = Assert.Single(NotificationCommands.Build(HostOs.Linux, new string('T', 200), body));

        string title = command.Arguments[2];
        string shown = command.Arguments[3];
        Assert.Equal(80, title.Length);
        Assert.EndsWith("...", title);
        Assert.Equal(250, shown.Length);
        Assert.StartsWith("first  second third xxx", shown);
        Assert.DoesNotContain('\n', shown);
    }

    [Fact]
    public void The_notifier_sends_the_same_texts_as_the_Windows_app()
    {
        var sent = new List<ShellCommand>();
        var notifier = new SystemNotifier(HostOs.Linux, c => { sent.Add(c); return true; }, work => work());

        notifier.NotifyDownloadComplete("a.mp4");
        notifier.NotifyDownloadStarted(1);
        notifier.NotifyDownloadStarted(3);

        Assert.Equal(new[] { "Download Complete", "Download started", "Download started" }, sent.Select(c => c.Arguments[2]));
        Assert.Equal(new[] { "\"a.mp4\" finished downloading.", "A download from your browser was added.", "3 downloads from your browser were added." }, sent.Select(c => c.Arguments[3]));
    }

    [Fact]
    public void A_tool_that_fails_or_is_missing_never_throws_and_is_reported_as_not_shown()
    {
        var failing = new SystemNotifier(HostOs.Linux, _ => false, work => work());
        var missing = new SystemNotifier(HostOs.MacOS, _ => throw new System.ComponentModel.Win32Exception("not installed"), work => work());

        failing.NotifyDownloadComplete("a.mp4");   // must not throw
        missing.NotifyDownloadComplete("a.mp4");

        Assert.False(failing.Send("t", "b"));
        Assert.False(missing.Send("t", "b"));
        Assert.True(new SystemNotifier(HostOs.Linux, _ => true, work => work()).Send("t", "b"));
    }
}
