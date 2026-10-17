using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests;

/// <summary>The copy-log / copy-error buttons (from main) now go through IClipboardService so Core stays free of WPF.</summary>
public class DownloadItemViewModelTests
{
    private sealed class FakeClipboard : IClipboardService
    {
        public List<string> Copied { get; } = new();
        public void SetText(string text) => Copied.Add(text);
    }

    private sealed class LockedClipboard : IClipboardService
    {
        public void SetText(string text) => throw new InvalidOperationException("clipboard is locked by another application");
    }

    [Fact]
    public void Copy_log_puts_the_complete_log_on_the_clipboard()
    {
        var task = new DownloadTask { Command = "yt-dlp x" };
        for (int i = 1; i <= 150; i++) task.AppendLog("line " + i); // more than the 100 lines kept on screen
        var clipboard = new FakeClipboard();
        var vm = new DownloadItemViewModel(task, clipboard);

        vm.CopyLogCommand.Execute(null);

        var copied = Assert.Single(clipboard.Copied);
        Assert.Contains("line 1" + Environment.NewLine, copied);
        Assert.Contains("line 150", copied);
    }

    [Fact]
    public void Copy_error_puts_the_error_message_on_the_clipboard()
    {
        var task = new DownloadTask { Command = "yt-dlp x", ErrorMessage = "Vimeo needs you to be logged in." };
        var clipboard = new FakeClipboard();
        var vm = new DownloadItemViewModel(task, clipboard);

        vm.CopyErrorCommand.Execute(null);

        Assert.Equal("Vimeo needs you to be logged in.", Assert.Single(clipboard.Copied));
    }

    [Fact]
    public void Nothing_is_copied_when_there_is_nothing_to_copy()
    {
        var clipboard = new FakeClipboard();
        var vm = new DownloadItemViewModel(new DownloadTask { Command = "yt-dlp x" }, clipboard);

        vm.CopyLogCommand.Execute(null);
        vm.CopyErrorCommand.Execute(null);

        Assert.Empty(clipboard.Copied);
    }

    [Fact]
    public void A_locked_clipboard_never_crashes_the_ui()
    {
        var task = new DownloadTask { Command = "yt-dlp x" };
        task.AppendLog("something");
        var vm = new DownloadItemViewModel(task, new LockedClipboard());

        var ex = Record.Exception(() => vm.CopyLogCommand.Execute(null));

        Assert.Null(ex);
    }

    [Fact]
    public void Without_a_clipboard_service_the_commands_do_nothing()
    {
        var task = new DownloadTask { Command = "yt-dlp x" };
        task.AppendLog("something");
        var vm = new DownloadItemViewModel(task);

        var ex = Record.Exception(() => vm.CopyLogCommand.Execute(null));

        Assert.Null(ex);
    }
}
