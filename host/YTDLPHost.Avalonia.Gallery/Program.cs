using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using YTDLPHost.Models;
using YTDLPHost.Native;
using YTDLPHost.Services;
using YTDLPHost.UI;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Gallery;

/// <summary>The headless session builds the app from this: the real <see cref="App"/> (its styles), real Skia drawing, no window on screen.</summary>
public static class GalleryEntry
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class Program
{
    private static int Main(string[] args)
    {
        string outDir = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "ytdlp-gallery");
        Directory.CreateDirectory(outDir);

        // Everything the view model reads or writes (settings, history, logs, engine) lives in a throw-away folder.
        AppPaths.Current = new SandboxPaths();
        SeedEngineFiles();   // empty stand-ins, so the first-run setup does not download anything

        int written = 0;
        using (var session = HeadlessUnitTestSession.StartNew(typeof(GalleryEntry)))
        {
            written = session.Dispatch(() => Render(outDir), CancellationToken.None).GetAwaiter().GetResult();
        }

        Console.WriteLine($"{written} picture(s) in {outDir}");
        Environment.Exit(written > 0 ? 0 : 1);   // the view model starts background work; do not wait for it
        return 0;
    }

    private static int Render(string outDir)
    {
        var services = new PlatformServices(
            new AvaloniaUiDispatcher(),
            new AvaloniaDialogService(() => null),
            new AvaloniaAppLifetime(() => null, () => { }),
            new AvaloniaClipboardService(() => null),
            new QuietTray(),
            new QuietNotifier(),
            new QuietShell());

        var vm = new MainViewModel(services);
        var window = new MainWindow(vm);
        window.Show();
        var app = Application.Current!;

        int count = 0;
        count += Capture(window, outDir, "01-empty");

        // The owner's own screenshot: one download running and one waiting (names as yt-dlp's safe file names deliver them).
        var live = Card(t =>
        {
            t.Title = "I_Tested_The_World_s_Most_Dangerous_Animals"; t.Resolution = "360p"; t.Status = DownloadStatus.Downloading;
            t.Progress = 11.2; t.Speed = "2.14MiB/s"; t.Eta = "00:30"; t.FileSize = "74.53MiB"; t.CurrentPhase = "Downloading Video...";
        });
        var waiting = Card(t => { t.Title = "Unknown"; t.Resolution = "1080p"; t.Status = DownloadStatus.Queued; t.CurrentPhase = "Queued"; });
        vm.Downloads.Add(live); vm.Downloads.Add(waiting);
        count += Capture(window, outDir, "01b-two-downloads");
        vm.Downloads.Remove(live); vm.Downloads.Remove(waiting);

        // Cards in every state (no "Queued" one: the queue would try to start it).
        var running = Card(t =>
        {
            t.Title = "A_sample_video_with_a_very_long_title_that_must_be_trimmed_at_the_end_of_the_line_instead_of_wrapping_1080p";
            t.Resolution = "1080p"; t.Status = DownloadStatus.Downloading; t.Progress = 42.5;
            t.Speed = "3.2MiB/s"; t.Eta = "00:41"; t.FileSize = "128.4MiB"; t.CurrentPhase = "Downloading video";
        });
        var preparing = Card(t =>
        {
            t.Title = "Unknown"; t.Status = DownloadStatus.Downloading; t.IsIndeterminate = true; t.CurrentPhase = "Preparing...";
            t.AppendLog("[youtube] Extracting URL: https://example.invalid/watch?v=sample");
            t.AppendLog("[youtube] sample: Downloading webpage");
            t.AppendLog("[info] Available formats for sample: 137 mp4 1080p, 140 m4a 128k");
            t.AppendLog("[download]  12.0% of 80.00MiB at 3.10MiB/s ETA 00:22");
            t.AppendLog("[download]  18.5% of 80.00MiB at 3.20MiB/s ETA 00:20");
        });
        var merging = Card(t =>
        {
            t.Title = "Merging_sample_2160p"; t.Resolution = "2160p"; t.Status = DownloadStatus.Downloading; t.Progress = 100; t.CurrentPhase = "Merging video and audio";
        });
        var paused = Card(t =>
        {
            t.Title = "Paused sample"; t.Resolution = "720p"; t.Status = DownloadStatus.Paused; t.Progress = 60; t.FileSize = "80.0MiB";
        });
        var done = Card(t =>
        {
            t.Title = "The_Finished_Sample_1080p"; t.Resolution = "1080p"; t.Status = DownloadStatus.Completed; t.Progress = 100; t.CurrentPhase = "Completed";
        });
        var failed = Card(t =>
        {
            t.Title = "Failed sample"; t.Status = DownloadStatus.Error; t.Progress = 12;
            t.ErrorMessage = "ERROR: [youtube] sample: Sign in to confirm you're not a bot. Use --cookies-from-browser or --cookies for the authentication.";
        });
        var cancelled = Card(t => { t.Title = "Cancelled sample"; t.Status = DownloadStatus.Cancelled; });

        foreach (var card in new[] { running, preparing, paused, done, failed }) vm.Downloads.Add(card);
        vm.StatusText = "Downloading: A sample video with a very long title...";
        count += Capture(window, outDir, "02-cards");

        // Hover: the secondary actions (log, cancel, remove) appear on the card under the pointer.
        SettleAnimations();
        var hovered = Cards(window).Skip(1).FirstOrDefault();
        if (hovered != null) { SetPointerOver(hovered, true); count += Capture(window, outDir, "02b-hover-reveals-actions"); SetPointerOver(hovered, false); }

        // Keyboard: Tab to a button shows the focus ring and reveals the card's actions too.
        try
        {
            var pause = Cards(window).Skip(1).FirstOrDefault()?.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => Equals(AutomationProperties.GetName(b), "Pause"));
            pause?.Focus(NavigationMethod.Tab);
            count += Capture(window, outDir, "02c-keyboard-focus");
            window.Focus();
        }
        catch (Exception ex) { Console.WriteLine($"02c-keyboard-focus: failed: {ex.Message}"); }

        preparing.IsLogVisible = true;
        count += Capture(window, outDir, "03-log-open");
        preparing.IsLogVisible = false;

        try
        {
            var gear = window.FindControl<Button>("SettingsButton");
            gear?.Flyout?.ShowAt(gear);
            count += Capture(window, outDir, "04-settings-menu");
            gear?.Flyout?.Hide();
        }
        catch (Exception ex) { Console.WriteLine($"04-settings-menu: failed: {ex.Message}"); }

        // The Limits panel, with a speed cap so the button shows it; and the toast.
        vm.Settings.SpeedLimit = "5M"; vm.Queue.Refresh();
        window.DataContext = null; window.DataContext = vm;   // the settings are plain values: bind again so the panel shows them
        var limits = window.FindControl<ToggleButton>("LimitsToggle");
        if (limits != null) limits.IsChecked = true;
        vm.Toast.Show("Browser link repaired. The extension can open this app again.");
        count += Capture(window, outDir, "05-limits-panel-and-toast");
        if (limits != null) limits.IsChecked = false;
        vm.Toast.Dismiss();

        // Tabs: Failed, then the glyph set (processing, cancelled) on the All tab, with Clear finished in the footer.
        vm.Queue.Filter = QueueFilter.Failed;
        count += Capture(window, outDir, "06-failed-tab");
        vm.Queue.Filter = QueueFilter.Done;
        count += Capture(window, outDir, "07-completed-tab");
        vm.Queue.Filter = QueueFilter.All;
        vm.Downloads.Remove(running); vm.Downloads.Remove(preparing);
        vm.Downloads.Insert(0, merging); vm.Downloads.Add(cancelled);
        vm.Toast.Show("Browser link disconnected.", "Undo", () => { });
        count += Capture(window, outDir, "08-glyphs-processing-cancelled");
        vm.Toast.Dismiss();
        vm.Downloads.Remove(merging); vm.Downloads.Remove(cancelled);
        vm.Downloads.Insert(0, running); vm.Downloads.Insert(1, preparing);

        // The light theme.
        app.RequestedThemeVariant = ThemeVariant.Light;
        count += Capture(window, outDir, "09-light-cards");
        preparing.IsLogVisible = true;
        count += Capture(window, outDir, "10-light-log-open");
        preparing.IsLogVisible = false;
        try
        {
            var gear = window.FindControl<Button>("SettingsButton");
            gear?.Flyout?.ShowAt(gear);
            count += Capture(window, outDir, "11-light-settings-menu");
            gear?.Flyout?.Hide();
        }
        catch (Exception ex) { Console.WriteLine($"11-light-settings-menu: failed: {ex.Message}"); }
        if (limits != null) limits.IsChecked = true;
        count += Capture(window, outDir, "12-light-limits-panel");
        if (limits != null) limits.IsChecked = false;
        app.RequestedThemeVariant = ThemeVariant.Dark;

        // The Browser integration panel with sample results (one browser of each state).
        try
        {
            var report = new NativeHostReport(true, "Some browsers are not linked to this install. Press Repair.", new[]
            {
                new BrowserLinkStatus("Chrome", BrowserLinkState.Linked, "Linked to this install."),
                new BrowserLinkStatus("Chromium", BrowserLinkState.NotInstalled, "Not installed."),
                new BrowserLinkStatus("Edge", BrowserLinkState.OutOfDate, "Points to another install: /opt/old-ytdlp/ytdlp-shim"),
                new BrowserLinkStatus("Brave", BrowserLinkState.NotLinked, "Not linked.")
            });
            var panelModel = new BrowserIntegrationViewModel(() => report, () => new[] { "Edge: registered /home/user/.config/microsoft-edge/NativeMessagingHosts/com.ytdlppro.host.json.", "Brave: registered /home/user/.config/BraveSoftware/Brave-Browser/NativeMessagingHosts/com.ytdlppro.host.json." });
            var panel = new BrowserIntegrationWindow(panelModel);
            panel.Show();
            count += Capture(panel, outDir, "13-browser-integration");
            panelModel.RepairCommand.Execute(null);
            app.RequestedThemeVariant = ThemeVariant.Light;
            count += Capture(panel, outDir, "14-browser-integration-light-after-repair");
            app.RequestedThemeVariant = ThemeVariant.Dark;
            panel.Close();
        }
        catch (Exception ex) { Console.WriteLine($"13-browser-integration: failed: {ex.Message}"); }

        window.Width = window.MinWidth; window.Height = window.MinHeight;
        count += Capture(window, outDir, "15-minimum-size");
        if (limits != null) limits.IsChecked = true;
        count += Capture(window, outDir, "16-minimum-size-limits-open");
        if (limits != null) limits.IsChecked = false;

        return count;
    }

    /// <summary>Lets queued UI work and short animations (150 to 250 ms) finish, so a picture shows the state and not the middle of a transition.</summary>
    private static void SettleAnimations()
    {
        for (int i = 0; i < 45; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(8);
        }
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The sandbox has no ffmpeg, so the app adds its "Initial System Setup" card here. It is real behaviour but would hide what a picture is about.</summary>
    private static void RemoveSandboxSetupCard(Window window)
    {
        if (window.DataContext is not MainViewModel vm) return;
        foreach (var card in vm.Downloads.Where(d => d.Task.Title == EngineSetup.SetupCardTitle).ToList()) vm.Downloads.Remove(card);
        if (vm.Downloads.Count == 0 && vm.StatusText.Contains("FFmpeg", StringComparison.OrdinalIgnoreCase)) vm.StatusText = "Ready";
    }

    private static IEnumerable<Border> Cards(Window window) =>
        window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("card"));

    private static void SetPointerOver(Border card, bool on) => ((IPseudoClasses)card.Classes).Set(":pointerover", on);

    private static DownloadItemViewModel Card(Action<DownloadTask> setup)
    {
        var task = new DownloadTask();
        setup(task);
        return new DownloadItemViewModel(task);
    }

    private static int Capture(Window window, string dir, string name)
    {
        try
        {
            RemoveSandboxSetupCard(window);
            SettleAnimations();
            var frame = window.CaptureRenderedFrame();
            if (frame is null) { Console.WriteLine($"{name}: no frame was rendered (is the Skia renderer on?)"); return 0; }
            string path = Path.Combine(dir, name + ".png");
            frame.Save(path);
            Console.WriteLine("Wrote " + path);
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{name}: failed: {ex.Message}");
            return 0;
        }
    }

    private static void SeedEngineFiles()
    {
        var tools = new EngineProvisioner(AppPaths.Current, new HttpClient()).CurrentTools();
        foreach (string path in new[] { tools.YtDlpPath, tools.DenoPath, tools.FfmpegPath })
        {
            if (string.IsNullOrEmpty(path)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllBytes(path, Array.Empty<byte>());
        }
    }

    private sealed class SandboxPaths : IAppPaths
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-gallery-run-" + Guid.NewGuid());
        public string HomeDir => Path.Combine(_root, "home");
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(_root, "engine");
        public string DataDir => Path.Combine(_root, "data");
        public string LogsDir => Path.Combine(_root, "logs");
        public string PayloadsDir => Path.Combine(_root, "payloads");
        public string TempDir => Path.Combine(_root, "tmp");
    }

#pragma warning disable CS0067 // events of the interfaces that nothing raises here
    private sealed class QuietTray : ITrayService
    {
        public event EventHandler? ShowWindowRequested;
        public event EventHandler? ExitRequested;
        public void Initialize() { }
        public void UpdateTooltip(string text) { }
        public void Dispose() { }
    }

    private sealed class QuietNotifier : INotifier
    {
        public event EventHandler? Activated;
        public void NotifyDownloadComplete(string fileName) { }
        public void NotifyDownloadStarted(int count) { }
    }
#pragma warning restore CS0067

    private sealed class QuietShell : IShellActions
    {
        public void RevealFile(string path) { }
        public void OpenFolder(string path) { }
        public void OpenFile(string path) { }
    }
}
