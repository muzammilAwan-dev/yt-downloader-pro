using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using YTDLPHost.Models;

namespace YTDLPHost.Services
{
    public static class HistoryManager
    {
        private static readonly string HistoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloaderProEngine", "history.json");
        private static readonly object _fileLock = new();

        public static void SaveHistory(IEnumerable<DownloadTask> tasks)
        {
            // Lock ensures thread safety if multiple downloads finish simultaneously
            lock (_fileLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(HistoryPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                    
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(tasks, options);
                    File.WriteAllText(HistoryPath, json);
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[HISTORY] Failed to save queue state: {ex.Message}");
                }
            }
        }

        public static List<DownloadTask> LoadHistory()
        {
            lock (_fileLock)
            {
                try
                {
                    if (File.Exists(HistoryPath))
                    {
                        var json = File.ReadAllText(HistoryPath);
                        var tasks = JsonSerializer.Deserialize<List<DownloadTask>>(json);
                        if (tasks != null)
                        {
                            foreach (var task in tasks)
                            {
                                // Auto-Recovery: Reset active items to paused if the app was abruptly closed
                                if (task.Status == DownloadStatus.Downloading || task.Status == DownloadStatus.Queued)
                                {
                                    task.Status = DownloadStatus.Paused;
                                    task.CurrentPhase = "Paused (System Recovered)";
                                    task.Speed = "";
                                    task.Eta = "";
                                }
                            }
                            return tasks;
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"[HISTORY] Failed to load queue state: {ex.Message}");
                }
                return new List<DownloadTask>();
            }
        }
    }
}