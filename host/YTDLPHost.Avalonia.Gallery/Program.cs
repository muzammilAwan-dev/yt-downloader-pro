using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

        int count = 0;
        count += Capture(window, outDir, "01-empty");

        // Cards in every state (no "Queued" one: the queue would try to start it).
        var running = Card(t =>
        {
            t.Title = "A sample video with a very long title that must be trimmed at the end of the line instead of wrapping";
            t.Resolution = "1080p"; t.Status = DownloadStatus.Downloading; t.Progress = 42.5;
            t.Speed = "3.2MiB/s"; t.Eta = "00:41"; t.FileSize = "128.4MiB"; t.CurrentPhase = "Downloading video";
        });
        var preparing = Card(t =>
        {
            t.Title = "Fetching video information"; t.Status = DownloadStatus.Downloading; t.IsIndeterminate = true; t.CurrentPhase = "Preparing...";
            t.AppendLog("[youtube] Extracting URL: https://example.invalid/watch?v=sample");
            t.AppendLog("[youtube] sample: Downloading webpage");
            t.AppendLog("[info] Available formats for sample: 137 mp4 1080p, 140 m4a 128k");
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

        foreach (var card in new[] { running, preparing, paused, done, failed }) vm.Downloads.Add(card);
        vm.StatusText = "Downloading: A sample video with a very long title...";
        count += Capture(window, outDir, "02-cards");

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

        // The first three cards out of the way, so the finished and the failed one (error banner) are fully visible.
        foreach (var card in new[] { running, preparing, paused }) vm.Downloads.Remove(card);
        count += Capture(window, outDir, "05-finished-and-failed");

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
            count += Capture(panel, outDir, "07-browser-integration");
            panelModel.RepairCommand.Execute(null);
            count += Capture(panel, outDir, "08-browser-integration-after-repair");
            panel.Close();
        }
        catch (Exception ex) { Console.WriteLine($"07-browser-integration: failed: {ex.Message}"); }

        window.Width = window.MinWidth; window.Height = window.MinHeight;
        count += Capture(window, outDir, "06-minimum-size");

        return count;
    }

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
