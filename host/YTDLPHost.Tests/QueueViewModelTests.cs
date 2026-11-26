using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests;

public class QueueViewModelTests
{
    private static DownloadItemViewModel Card(DownloadStatus status, string speed = "", string title = "t") =>
        new(new DownloadTask { Command = "x", Title = title, Status = status, Speed = speed, CurrentPhase = status == DownloadStatus.Downloading ? "Downloading video" : "" });

    private sealed class Rig
    {
        public ObservableCollection<DownloadItemViewModel> Source { get; } = new();
        public AppSettings Settings { get; } = new() { SpeedLimit = "0", MaxConcurrentDownloads = 3 };
        public string Status { get; set; } = "Ready";
        public QueueViewModel Queue { get; }
        public Rig() { Queue = new QueueViewModel(Source, new InlineUiDispatcher(), () => Status, () => Settings); }
    }

    [Fact]
    public void Starts_empty_and_ready()
    {
        var q = new Rig().Queue;
        Assert.False(q.HasAnyCards);
        Assert.False(q.IsFilterEmpty);          // the "No downloads yet" page covers an empty list
        Assert.Equal("Ready", q.FooterText);
        Assert.False(q.HasFooterSpeed);
        Assert.Equal("Limits", q.LimitsLabel);
    }

    [Fact]
    public void Tabs_show_from_four_cards_and_list_only_matching_cards()
    {
        var rig = new Rig();
        var done = Card(DownloadStatus.Completed); var failed = Card(DownloadStatus.Error);
        foreach (var c in new[] { Card(DownloadStatus.Downloading), Card(DownloadStatus.Paused), done }) rig.Source.Add(c);
        Assert.False(rig.Queue.ShowFilters);
        rig.Source.Add(failed);
        Assert.True(rig.Queue.ShowFilters);
        Assert.Equal((4, 2, 1, 1), (rig.Queue.AllCount, rig.Queue.ActiveCount, rig.Queue.DoneCount, rig.Queue.FailedCount));

        rig.Queue.Filter = QueueFilter.Failed;
        Assert.Equal(new[] { failed }, rig.Queue.Visible);
        rig.Queue.Filter = QueueFilter.Done;
        Assert.Equal(new[] { done }, rig.Queue.Visible);
        rig.Queue.Filter = QueueFilter.Active;
        Assert.Equal(2, rig.Queue.Visible.Count);
        rig.Queue.Filter = QueueFilter.All;
        Assert.Equal(rig.Source, rig.Queue.Visible);  // same cards, same order
    }

    [Fact]
    public void A_card_changing_state_moves_between_tabs_and_updates_the_counts()
    {
        var rig = new Rig();
        var running = Card(DownloadStatus.Downloading);
        foreach (var c in new[] { running, Card(DownloadStatus.Queued), Card(DownloadStatus.Queued), Card(DownloadStatus.Completed) }) rig.Source.Add(c);
        rig.Queue.Filter = QueueFilter.Active;
        Assert.Contains(running, rig.Queue.Visible);

        running.Task.Status = DownloadStatus.Completed;
        Assert.DoesNotContain(running, rig.Queue.Visible);
        Assert.Equal((2, 2), (rig.Queue.ActiveCount, rig.Queue.DoneCount));
    }

    [Fact]
    public void Shrinking_the_list_below_four_returns_to_the_all_tab()
    {
        var rig = new Rig();
        for (int i = 0; i < 4; i++) rig.Source.Add(Card(i == 0 ? DownloadStatus.Error : DownloadStatus.Completed));
        rig.Queue.Filter = QueueFilter.Failed;
        rig.Source.RemoveAt(3); rig.Source.RemoveAt(2);
        Assert.Equal(QueueFilter.All, rig.Queue.Filter);
        Assert.Equal(2, rig.Queue.Visible.Count);
    }

    [Fact]
    public void Empty_tab_says_so()
    {
        var rig = new Rig();
        for (int i = 0; i < 4; i++) rig.Source.Add(Card(DownloadStatus.Completed));
        rig.Queue.Filter = QueueFilter.Failed;
        Assert.True(rig.Queue.IsFilterEmpty);
        Assert.Equal("No failed downloads. Nice.", rig.Queue.FilterEmptyText);
    }

    [Fact]
    public void Footer_follows_the_queue_and_clear_finished_appears_with_finished_cards()
    {
        var rig = new Rig();
        rig.Source.Add(Card(DownloadStatus.Downloading, "2.00MiB/s")); rig.Source.Add(Card(DownloadStatus.Queued));
        Assert.Equal("1 downloading \u00B7 1 waiting", rig.Queue.FooterText);
        Assert.True(rig.Queue.IsBusy);
        Assert.False(rig.Queue.CanClearFinished);
        Assert.Equal("\u2193 2.00 MiB/s", rig.Queue.FooterSpeedText);

        rig.Source.Add(Card(DownloadStatus.Cancelled));
        Assert.True(rig.Queue.CanClearFinished);

        rig.Status = "Engine updated to 2026.11.01"; rig.Queue.Refresh();
        Assert.Equal("Engine updated to 2026.11.01", rig.Queue.FooterText);
    }

    [Fact]
    public void Total_speed_follows_the_cards_without_a_status_change()
    {
        var rig = new Rig();
        var a = Card(DownloadStatus.Downloading, "1.00MiB/s");
        rig.Source.Add(a);
        a.Task.Speed = "4.00MiB/s";
        // the card batches its live refresh; give it time
        SpinWait.SpinUntil(() => rig.Queue.FooterSpeedText == "\u2193 4.00 MiB/s", TimeSpan.FromSeconds(2));
        Assert.Equal("\u2193 4.00 MiB/s", rig.Queue.FooterSpeedText);
    }

    [Fact]
    public void Limits_button_shows_a_cap_after_refresh()
    {
        var rig = new Rig();
        rig.Settings.SpeedLimit = "2.5M"; rig.Settings.MaxConcurrentDownloads = 2;
        rig.Queue.Refresh();
        Assert.Equal("Limit 2.5M/s", rig.Queue.LimitsLabel);
        Assert.Equal("2 downloads at once \u00B7 2.5M/s speed limit", rig.Queue.LimitsTooltip);
    }

    [Fact]
    public void Removed_cards_stop_being_watched_and_leave_the_visible_list()
    {
        var rig = new Rig();
        var c = Card(DownloadStatus.Completed);
        rig.Source.Add(c);
        rig.Source.Remove(c);
        Assert.Empty(rig.Queue.Visible);
        c.Task.Status = DownloadStatus.Error;          // a removed card must not change anything
        Assert.Equal(0, rig.Queue.AllCount);
    }
}

public class ToastViewModelTests
{
    private static ToastViewModel Toast() => new(new InlineUiDispatcher(), TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(200));

    [Fact]
    public async Task Message_shows_then_disappears_by_itself()
    {
        var t = Toast();
        t.Show("Browser link repaired.");
        Assert.True(t.IsVisible);
        Assert.Equal("Browser link repaired.", t.Message);
        Assert.False(t.HasAction);
        await Task.Delay(400);
        Assert.False(t.IsVisible);
    }

    [Fact]
    public void Action_dismisses_the_toast_then_runs()
    {
        var t = Toast();
        bool ran = false, visibleWhenRun = true;
        t.Show("Disconnected.", "Undo", () => { ran = true; visibleWhenRun = t.IsVisible; });
        Assert.True(t.HasAction);
        Assert.Equal("Undo", t.ActionLabel);
        t.RunAction();
        Assert.True(ran);
        Assert.False(visibleWhenRun);
        Assert.False(t.IsVisible);
    }

    [Fact]
    public async Task A_newer_toast_is_not_closed_by_the_older_ones_timer()
    {
        var t = Toast();
        t.Show("first");
        await Task.Delay(50);
        t.Show("second", "Undo", () => { });
        await Task.Delay(100);              // the first timer (80 ms) has fired by now
        Assert.True(t.IsVisible);
        Assert.Equal("second", t.Message);
    }

    [Fact]
    public void Running_an_action_twice_does_nothing_the_second_time()
    {
        var t = Toast(); int runs = 0;
        t.Show("x", "Undo", () => runs++);
        t.RunAction(); t.RunAction();
        Assert.Equal(1, runs);
    }
}
