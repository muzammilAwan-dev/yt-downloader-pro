using System.Collections.Generic;
using System.Linq;
using YTDLPHost.Models;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Services
{
    public enum QueueFilter { All, Active, Done, Failed }

    /// <summary>How many downloads are in each state. Active = queued + downloading + paused.</summary>
    public readonly record struct QueueCounts(int All, int Active, int Done, int Failed, int Cancelled, int Downloading, int Waiting, int Paused);

    /// <summary>
    /// The queue's list logic (filter tabs, counts, footer, speed total, limits label) without any UI types, so the WPF window and the
    /// Avalonia window show exactly the same words and numbers, and the rules can be unit-tested.
    /// </summary>
    public static class QueueSummary
    {
        /// <summary>Filters only earn their place once there is a longer list to sort through; with a few cards they are noise.</summary>
        public const int MinCardsForFilters = 4;

        public static bool ShowFilters(int totalCards) => totalCards >= MinCardsForFilters;

        public static bool Matches(QueueFilter filter, DownloadStatus status) => filter switch
        {
            QueueFilter.Active => status == DownloadStatus.Queued || status == DownloadStatus.Downloading || status == DownloadStatus.Paused,
            QueueFilter.Done => status == DownloadStatus.Completed,
            QueueFilter.Failed => status == DownloadStatus.Error,
            _ => true
        };

        public static QueueCounts Count(IEnumerable<DownloadStatus> statuses)
        {
            int all = 0, active = 0, done = 0, failed = 0, cancelled = 0, downloading = 0, waiting = 0, paused = 0;
            foreach (var status in statuses)
            {
                all++;
                switch (status)
                {
                    case DownloadStatus.Queued: active++; waiting++; break;
                    case DownloadStatus.Downloading: active++; downloading++; break;
                    case DownloadStatus.Paused: active++; paused++; break;
                    case DownloadStatus.Completed: done++; break;
                    case DownloadStatus.Error: failed++; break;
                    case DownloadStatus.Cancelled: cancelled++; break;
                }
            }
            return new QueueCounts(all, active, done, failed, cancelled, downloading, waiting, paused);
        }

        /// <summary>"Clear finished" appears only when something finished or was cancelled.</summary>
        public static bool CanClearFinished(QueueCounts counts) => counts.Done + counts.Cancelled > 0;

        /// <summary>What the list says when the chosen tab has no cards.</summary>
        public static string EmptyFilterMessage(QueueFilter filter) => filter switch
        {
            QueueFilter.Active => "Nothing is downloading right now.",
            QueueFilter.Done => "No completed downloads yet.",
            QueueFilter.Failed => "No failed downloads. Nice.",
            _ => ""
        };

        /// <summary>
        /// The footer says what the queue is doing right now ("1 downloading · 2 waiting") instead of the last event
        /// ("Added: Fetching video..."). Real messages from the app (engine updates, network errors) still show as they are.
        /// </summary>
        public static string FooterText(string? statusText, QueueCounts counts)
        {
            string message = statusText ?? "";
            bool generic = message.Length == 0 || message == "Ready" || message == "All downloads complete" ||
                           message.StartsWith("Added:", System.StringComparison.Ordinal) || message.StartsWith("Downloading:", System.StringComparison.Ordinal);
            if (!generic) return message;

            var parts = new List<string>();
            if (counts.Downloading > 0) parts.Add($"{counts.Downloading} downloading");
            if (counts.Waiting > 0) parts.Add($"{counts.Waiting} waiting");
            if (counts.Paused > 0) parts.Add($"{counts.Paused} paused");

            return parts.Count > 0
                ? string.Join(" \u00B7 ", parts)
                : (counts.All == 0 ? "Ready" : "All downloads finished");
        }

        /// <summary>Total download speed for the footer, e.g. "↓ 4.10 MiB/s"; null when nothing is moving bytes.</summary>
        public static string? TotalSpeedText(IEnumerable<DownloadItemViewModel> items)
        {
            double total = 0;
            foreach (var d in items)
            {
                // Merging / converting moves no bytes, so it adds no speed.
                if (d.Task.Status == DownloadStatus.Downloading && !d.IsProcessing) total += RateMath.ParseRate(d.Task.Speed);
            }
            if (total <= 0) return null;
            var (value, unit) = RateMath.Format(total);
            return $"\u2193 {value} {unit}";
        }

        /// <summary>Text on the Limits button. A speed cap is the one setting that quietly changes behaviour, so it is shown there.</summary>
        public static string LimitsLabel(string? speedLimit)
        {
            string speed = (speedLimit ?? "0").Trim();
            return IsCapped(speed) ? $"Limit {speed.ToUpperInvariant()}/s" : "Limits";
        }

        public static string LimitsTooltip(int maxConcurrentDownloads, string? speedLimit)
        {
            string speed = (speedLimit ?? "0").Trim();
            return $"{maxConcurrentDownloads} downloads at once \u00B7 {(IsCapped(speed) ? speed.ToUpperInvariant() + "/s speed limit" : "no speed limit")}";
        }

        private static bool IsCapped(string speed) => speed.Length > 0 && speed != "0";
    }
}
