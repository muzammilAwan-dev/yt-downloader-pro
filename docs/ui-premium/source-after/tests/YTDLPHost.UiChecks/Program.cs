using YTDLPHost.Models;
using YTDLPHost.ViewModels;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Reflection;
using System.Windows.Automation;

// No Application/MainViewModel startup, persistence, clipboard or engine actions.
// Dependency-free presentation regression checks, not a live download/UIA test.
internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 4 && args[0] == "--uia")
                return InspectExternalWindow(new IntPtr(long.Parse(args[1])), int.Parse(args[2]), args[3]);
            foreach (var title in new[] { "Unknown", "", "Fetching video...", "A known title" })
            foreach (var status in Enum.GetValues<DownloadStatus>())
            {
                var item = new DownloadItemViewModel(new DownloadTask
                {
                    Title = title, Status = status,
                    UrlPayload = "SYNTHETIC_SESSION_SENTINEL_command_and_cookies"
                });
                Check(item.TitleTooltip == item.DisplayTitle, "Tooltip uses safe display title");
                Check(!item.TitleTooltip.Contains("SYNTHETIC_SESSION_SENTINEL"), "Payload absent from tooltip");
                Check(!item.ProgressAutomationName.Contains("SYNTHETIC_SESSION_SENTINEL"), "Payload absent from progress name");
                if (title != "A known title" && status != DownloadStatus.Downloading)
                    Check(item.DisplayTitle == "Untitled download", "Waiting/settled unknown titles do not claim fetching");
            }

            foreach (var phase in new[] { "Merging video and audio...", "Converting Audio...", "Embedding metadata...", "Adding metadata...", "Removing Sponsors..." })
            foreach (bool indeterminate in new[] { true, false })
            {
                var item = new DownloadItemViewModel(new DownloadTask
                {
                    Title = "Synthetic media", Status = DownloadStatus.Downloading,
                    CurrentPhase = phase, Progress = 100, FileSize = "128 MiB",
                    Speed = "3.30MiB/s", Eta = "00:00", IsIndeterminate = indeterminate
                });
                Check(item.IsProcessing, "Recognizes processing phase");
                Check(!item.IsTransferMetricsVisible, "Processing hides transfer metrics");
                Check(item.SpeedText == "" && item.SpeedValue == "" && item.SpeedUnit == "" && item.EtaText == "", "Stale processing metrics suppressed");
                Check(!item.StatusLine.Contains("100") && !item.StatusLine.Contains("128"), "Processing has no transfer percentage/size");
                Check(item.IsProgressIndeterminate, "Processing does not imply 100% completion");
                Check(!item.IsCompleted && item.CanBePaused && item.CanBeCancelled, "Processing action contract retained");
            }

            var transfer = new DownloadItemViewModel(new DownloadTask
            {
                Status = DownloadStatus.Downloading, CurrentPhase = "Downloading video...",
                Progress = 42.6, Speed = "3.30MiB/s", Eta = "00:22", FileSize = "128.40 MiB"
            });
            Check(transfer.IsTransferMetricsVisible && !transfer.IsProgressIndeterminate, "Determinate transfer state");
            Check(transfer.SpeedValue == "3.30" && transfer.SpeedUnit == "MiB/s" && transfer.EtaText == "ETA 00:22", "Known transfer metrics retained");
            Check(transfer.StatusLine.Contains("42.6%") && transfer.StatusLine.Contains("128.40 MiB"), "Transfer status retained");
            transfer.Task.IsIndeterminate = true;
            Check(transfer.EtaText == "00:22 elapsed", "Unknown-length transfer retains elapsed time");
            transfer.Task.Eta = "Unknown";
            Check(transfer.EtaText == "", "Unknown ETA not fabricated");
            transfer.Task.Status = DownloadStatus.Paused;
            Check(!transfer.IsProgressIndeterminate && transfer.IsProgressVisible && !transfer.IsTransferMetricsVisible, "Paused fill retained without sweep/metrics");
            Check(transfer.IsResumable && transfer.CanBeCancelled && !transfer.CanBeRemoved, "Paused actions retained");

            foreach (var status in Enum.GetValues<DownloadStatus>())
            {
                transfer.Task.Status = status;
                Check(transfer.CanBePaused == (status is DownloadStatus.Queued or DownloadStatus.Downloading), "Pause availability unchanged");
                Check(transfer.CanBeCancelled == (status is DownloadStatus.Queued or DownloadStatus.Downloading or DownloadStatus.Paused), "Cancel availability unchanged");
                Check(transfer.CanBeRemoved == (status is DownloadStatus.Completed or DownloadStatus.Error or DownloadStatus.Cancelled), "Remove availability unchanged");
            }

            transfer.Task.Title = "My_video_title_1080p";
            Check(transfer.DisplayTitle == "My video title", "Existing title cleanup retained");
            transfer.Task.ErrorMessage = "ERROR: HTTP Error 403: Forbidden";
            Check(transfer.ErrorHint.Contains("Settings and tools"), "Recovery names the actual menu");
            int presentationChecks = _checks;
            var policy = typeof(YTDLPHost.Services.ThemeService).Assembly
                .GetType("YTDLPHost.Services.WindowMaterialService")!
                .GetMethod("CanUseMica", BindingFlags.Static | BindingFlags.NonPublic)!;
            for (int mask = 0; mask < 128; mask++)
            {
                var flags = Enumerable.Range(0, 7).Select(i => (mask & (1 << i)) != 0).ToArray();
                bool expected = flags[0] && flags[1] && !flags[2] && flags[3] && !flags[4] && !flags[5] && flags[6];
                Check((bool)policy.Invoke(null, flags.Cast<object>().ToArray())! == expected, "Mica policy requires support/native frame/effects/composition and excludes contrast/saver/software rendering");
            }
            Console.WriteLine($"PASS: {presentationChecks} presentation checks + {_checks - presentationChecks} material-policy combinations. No live downloads/startup/keyboard/Narrator coverage.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL after {_checks} checks: {ex.Message}");
            return 1;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    // Explicit opt-in: inspect only the supplied window, never the desktop tree.
    // This is an external UIA read, not a Narrator or keyboard interaction test.
    private static int InspectExternalWindow(IntPtr hwnd, int expectedProcessId, string output)
    {
        GetWindowThreadProcessId(hwnd, out uint owner);
        Check(owner == expectedProcessId, "Target HWND belongs to the caller-selected process");
        var root = AutomationElement.FromHandle(hwnd);
        var descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
        var rows = new List<object>();
        foreach (AutomationElement element in descendants)
        {
            var info = element.Current;
            bool hasRange = element.TryGetCurrentPattern(RangeValuePattern.Pattern, out _);
            bool hasToggle = element.TryGetCurrentPattern(TogglePattern.Pattern, out object toggle);
            rows.Add(new
            {
                info.Name, Type = info.ControlType.ProgrammaticName, info.IsKeyboardFocusable,
                RangeValue = hasRange, ToggleState = hasToggle ? ((TogglePattern)toggle).Current.ToggleState.ToString() : null
            });
        }
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"External UIA read: {rows.Count} elements in selected window. No controls invoked.");
        return 0;
    }
}
