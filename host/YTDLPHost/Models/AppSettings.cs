using System;
using System.IO;
using System.Text.Json;

namespace YTDLPHost.Models
{
    public class AppSettings
    {
        public int MaxConcurrentDownloads { get; set; } = 3;
        
        // "0" = Unlimited. yt-dlp accepts formats like "5M" (5 MB/s), "500K" (500 KB/s)
        public string SpeedLimit { get; set; } = "0";

        // yt-dlp release channel used by the startup self-update: "nightly" (default), "stable" or "master".
        // YouTube changes break extraction every few weeks; fixes land in nightly first, stable can lag by weeks.
        public string UpdateChannel { get; set; } = "nightly";

        // Emergency override for YouTube's player_client list, e.g. "default,tv". Empty (default) = let yt-dlp choose,
        // which is right: the upstream defaults are retuned in every release. Only set this as a temporary hotfix.
        public string YoutubePlayerClients { get; set; } = "";

        private static readonly string SettingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloaderProEngine", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}