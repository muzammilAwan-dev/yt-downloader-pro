using System.Runtime.CompilerServices;
using YTDLPHost.Services;

namespace YTDLPHost.Tests;

/// <summary>
/// Runs once before any test: points the app's paths at a throw-away folder so tests (and the
/// AppLogger inside Core) never write to the developer's real settings, history or log files.
/// </summary>
internal static class TestEnvironment
{
    private sealed class SandboxPaths : IAppPaths
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "ytdlp-test-run-" + Guid.NewGuid());
        public string HomeDir => Path.Combine(_root, "home");
        public string DownloadsDir => Path.Combine(HomeDir, "Downloads");
        public string EngineDir => Path.Combine(_root, "engine");
        public string DataDir => Path.Combine(_root, "data");
        public string LogsDir => Path.Combine(_root, "logs");
        public string PayloadsDir => Path.Combine(_root, "payloads");
        public string TempDir => Path.Combine(_root, "tmp");
    }

    [ModuleInitializer]
    internal static void Init() => AppPaths.Current = new SandboxPaths();
}
