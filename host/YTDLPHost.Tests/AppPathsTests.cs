using Xunit;
using YTDLPHost.Models;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

public class AppPathsTests
{
    private sealed class TempPaths : IAppPaths
    {
        private readonly string _root;
        private readonly string _logsDir;

        // Logs stay in the shared test sandbox: AppLogger binds to its folder once, on first use,
        // and keeps the file open, so it must never point inside a folder this test deletes.
        public TempPaths(string root, string logsDir)
        {
            _root = root;
            _logsDir = logsDir;
        }

        public string HomeDir => Path.Combine(_root, "home");
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(_root, "engine");
        public string DataDir => Path.Combine(_root, "data");
        public string LogsDir => _logsDir;
        public string PayloadsDir => Path.Combine(_root, "payloads");
        public string TempDir => Path.Combine(_root, "tmp");
    }

    [Fact]
    public void Legacy_layout_matches_what_existing_installs_use()
    {
        // Existing Windows users keep settings, history, engine and payloads where they are today.
        var p = new LegacyAppPaths();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.Equal(Path.Combine(local, "YTDownloaderProEngine"), p.EngineDir);
        Assert.Equal(p.EngineDir, p.DataDir);
        Assert.Equal(p.EngineDir, p.LogsDir);
        Assert.Equal(Path.Combine(local, "YT Downloader Pro", "Payloads"), p.PayloadsDir);
        Assert.Equal(Path.Combine(p.HomeDir, "Downloads"), p.DownloadsDir);
        Assert.Equal(Path.GetTempPath(), p.TempDir);
    }

    [Fact]
    public void Settings_are_stored_under_the_configured_data_dir()
    {
        var root = Path.Combine(Path.GetTempPath(), "ytdlp-tests-" + Guid.NewGuid());
        var saved = AppPaths.Current;
        try
        {
            AppPaths.Current = new TempPaths(root, saved.LogsDir);

            new AppSettings { MaxConcurrentDownloads = 7 }.Save();

            Assert.True(File.Exists(Path.Combine(root, "data", "settings.json")));
            Assert.Equal(7, AppSettings.Load().MaxConcurrentDownloads);
        }
        finally
        {
            AppPaths.Current = saved;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
