using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YTDLPHost.Services
{
    public static class AppLogger
    {
        private static readonly string LogDir = AppPaths.Current.LogsDir;
        private static readonly string LogFile = Path.Combine(LogDir, "host_debug.log");
        
        // OPTIMIZATION 1: High-Performance Asynchronous Logging Queue decouples disk I/O from the app UI
        private static readonly BlockingCollection<string> _logQueue = new();
        private static readonly CancellationTokenSource _cts = new();

        // OPTIMIZATION 2: Pre-compiled Regex for massive speed boost when scrubbing messy logs
        private static readonly Regex UrlScrubber = new(@"https?:\/\/[^\s\""\'\>\|]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static AppLogger()
        {
            if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
            
            // Prevent the log file from bloating indefinitely (Truncates if > 5MB)
            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 5 * 1024 * 1024)
            {
                File.WriteAllText(LogFile, string.Empty);
            }

            // Spin up a dedicated background thread to write logs seamlessly
            Task.Run(ProcessLogQueue, CancellationToken.None);
        }

        public static void Log(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;

            // PRIVACY & CLEANUP FIX: Scrub all raw URLs (like the BestBuy one) from logs
            if (message.Contains("http", StringComparison.OrdinalIgnoreCase))
            {
                message = UrlScrubber.Replace(message, "https://www.merriam-webster.com/dictionary/redacted");
            }

            // Truncate massive Base64 strings that accidentally slip through
            if (message.Length > 250) 
            {
                message = message.Substring(0, 250) + "... [TRUNCATED FOR LOG CLEANLINESS]";
            }

            _logQueue.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}");
        }

        private static void ProcessLogQueue()
        {
            try
            {
                using var writer = new StreamWriter(LogFile, true) { AutoFlush = true };
                foreach (var message in _logQueue.GetConsumingEnumerable(_cts.Token))
                {
                    writer.WriteLine(message);
                }
            }
            catch (OperationCanceledException) { }
            catch { /* Failsafe to prevent logging crashes from taking down the app */ }
        }

        public static void Shutdown()
        {
            _cts.Cancel();
            _logQueue.CompleteAdding();
        }
    }
}