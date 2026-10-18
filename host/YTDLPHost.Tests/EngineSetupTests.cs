using System.Collections.ObjectModel;
using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Tests;

/// <summary>The first-start engine install flow moved out of MainViewModel (step 1.5e), tested with a fake installer.</summary>
public class EngineSetupTests
{
    private sealed class FakeEngine : IEngineProvisioner
    {
        public bool Present { get; set; }
        public Func<Action<EngineStage>?, Task> Install { get; set; } = _ => Task.CompletedTask;
        public bool UpdateStarted { get; private set; }
        public bool AllPresent() => Present;
        public Task InstallMissingAsync(Action<EngineStage>? onStage = null) => Install(onStage);
        public void StartBackgroundUpdate() => UpdateStarted = true;
    }

    private sealed class InstantRunner : IDownloadRunner
    {
        public event EventHandler<ProgressEventArgs>? OnProgressUpdate;
        public event EventHandler<CompleteEventArgs>? OnDownloadComplete;
        public event EventHandler<DownloadErrorEventArgs>? OnDownloadError;
        public event EventHandler<ExtractedInfoEventArgs>? OnInfoExtracted;
        public Task ExecuteAsync(DownloadTask task) => Task.CompletedTask;
        public void Cancel() { }
        public void Dispose() { }
    }

    private sealed class Rig
    {
        private int _created;
        public InlineUiDispatcher Ui { get; } = new();
        public ObservableCollection<DownloadItemViewModel> Items { get; } = new();
        public List<string> Statuses { get; } = new();
        public FakeEngine Engine { get; } = new();
        public DownloadQueue Queue { get; }
        public EngineSetup Setup { get; }
        public int RunnersCreated => Volatile.Read(ref _created);

        public Rig()
        {
            Queue = new DownloadQueue(Items, Ui, () => { Interlocked.Increment(ref _created); return new InstantRunner(); }, () => 3, TimeSpan.Zero);
            Setup = new EngineSetup(Engine, Queue, Items, Ui, null, text => Statuses.Add(text), TimeSpan.Zero);
        }

        public void AddQueuedItem()
        {
            var task = new DownloadTask { Command = "yt-dlp \"https://www.youtube.com/watch?v=video000001\"", Title = "t" };
            Ui.Invoke(() => Items.Add(new DownloadItemViewModel(task)));
        }
    }

    private static async Task WaitFor(Func<bool> condition, string what, int timeoutMs = 5000)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs) throw new TimeoutException("Timed out waiting for: " + what);
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task When_every_tool_is_present_there_is_no_card_and_the_queue_starts()
    {
        var rig = new Rig { };
        rig.Engine.Present = true;
        rig.AddQueuedItem();

        await rig.Setup.RunAsync();

        await WaitFor(() => rig.RunnersCreated == 1, "queued item starts");
        Assert.Single(rig.Items);                 // just the queued item, no setup card
        Assert.True(rig.Engine.UpdateStarted);
        Assert.False(rig.Setup.HasFailed);
        rig.Queue.Dispose();
    }

    [Fact]
    public async Task Missing_tools_show_a_progress_card_that_is_removed_when_done()
    {
        var rig = new Rig();
        var phases = new List<string>();
        rig.Engine.Install = async onStage =>
        {
            onStage!(EngineStage.DownloadingYtDlp);
            phases.Add(rig.Items[0].Task.CurrentPhase);
            onStage(EngineStage.ExtractingFfmpeg);
            phases.Add(rig.Items[0].Task.CurrentPhase);
            await Task.Yield();
        };
        rig.AddQueuedItem(); // added at the end of the list; the card is inserted at index 0 while installing

        await rig.Setup.RunAsync();

        Assert.Equal(new[] { "Downloading yt-dlp engine...", "Extracting codecs..." }, phases);
        Assert.DoesNotContain(rig.Items, i => i.Task.Title == EngineSetup.SetupCardTitle); // removed again
        Assert.Contains("Ready", rig.Statuses);
        Assert.True(rig.Engine.UpdateStarted);
        await WaitFor(() => rig.RunnersCreated == 1, "queued item starts after the setup");
        rig.Queue.Dispose();
    }

    [Fact]
    public async Task A_failed_install_shows_the_error_on_the_card_and_keeps_the_queue_stopped()
    {
        var rig = new Rig();
        rig.Engine.Install = _ => throw new HttpRequestException("no network");
        rig.AddQueuedItem();

        await rig.Setup.RunAsync(); // never throws; the failure is reported through the card and the status text

        Assert.True(rig.Setup.HasFailed);
        var card = Assert.Single(rig.Items, i => i.Task.Title == EngineSetup.SetupCardTitle);
        Assert.Equal(DownloadStatus.Error, card.Task.Status);
        Assert.Equal("Setup Failed! Please restart the app.", card.Task.CurrentPhase);
        Assert.Contains("Network Error. Please check your internet connection.", rig.Statuses);

        await Task.Delay(150);
        Assert.Equal(0, rig.RunnersCreated);      // downloads must not start without the tools
        rig.Queue.Dispose();
    }
}
