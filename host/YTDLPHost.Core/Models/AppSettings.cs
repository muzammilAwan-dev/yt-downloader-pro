using System;
using System.IO;
using System.Text.Json;
using YTDLPHost.Services;

namespace YTDLPHost.Models
{
    public class AppSettings
    {
        public int MaxConcurrentDownloads { get; set; } = 3;
        
        // "0" = Unlimited. yt-dlp accepts formats like "5M" (5 MB/s), "500K" (500 KB/s)
        public string SpeedLimit { get; set; } = "0"; 

        private static string SettingsPath => Path.Combine(AppPaths.Current.DataDir, "settings.json");

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