using Xunit;
using YTDLPHost.Native;
using YTDLPHost.Services;

namespace YTDLPHost.Tests.Native;

public class NativeHostStatusTests
{
    private const string Home = "/home/user";
    private const string AppDir = "/opt/ytdlp";
    private const string Origin = "chrome-extension://abcdefghijklmnopabcdefghijklmnop/";

    private sealed class FakeRegistry : INativeHostRegistry
    {
        public Dictionary<string, string> Values { get; } = new();
        public bool Throws { get; set; }
        public string? GetDefaultValue(string subKey) => Throws ? throw new IOException("access denied") : Values.GetValueOrDefault(subKey);
        public void SetDefaultValue(string subKey, string value) => Values[subKey] = value;
        public void DeleteKey(string subKey) => Values.Remove(subKey);
    }

    private sealed class FakeFiles : INativeHostFiles
    {
        public Dictionary<string, string> Files { get; } = new();
        public HashSet<string> Dirs { get; } = new();
        public bool FileExists(string path) => Files.ContainsKey(path);
        public bool DirectoryExists(string path) => Dirs.Contains(path) || Files.Keys.Any(f => f.StartsWith(path + Path.DirectorySeparatorChar));
        public string? ReadAllText(string path) => Files.GetValueOrDefault(path);
        public void WriteAllText(string path, string content) => Files[path] = content;
        public void DeleteFile(string path) => Files.Remove(path);
    }

    private static string Shim => Path.Combine(AppDir, NativeHostRegistration.ShimFileNameFor(HostOs.Linux));
    private static UnixBrowser Chrome => NativeHostUnixRegistration.Browsers.First(b => b.Name == "Chrome");

    private static FakeFiles InstalledApp()
    {
        var files = new FakeFiles();
        files.Files[Shim] = "";
        files.Files[Path.Combine(AppDir, NativeHostRegistration.AllowedOriginsFileName)] = Origin + "\n";
        return files;
    }

    private static string ChromeManifest => NativeHostUnixRegistration.ManifestPath(Chrome, HostOs.Linux, Home);

    // ---- Windows ----

    private static Func<string, bool> WindowsFiles(params string[] present) => path => present.Contains(Path.GetFileName(path));

    private static readonly string[] AllWindowsFiles =
        { NativeHostRegistration.ShimFileName, NativeHostRegistration.AllowedOriginsFileName, NativeHostRegistration.ManifestFileName };

    [Fact]
    public void Windows_all_three_keys_pointing_here_are_linked()
    {
        var registry = new FakeRegistry();
        string manifest = Path.Combine(AppDir, NativeHostRegistration.ManifestFileName);
        foreach (var b in NativeHostRegistration.WindowsBrowsers) registry.Values[NativeHostRegistration.SubKeyFor(b)] = manifest;

        var report = NativeHostStatus.CheckWindows(AppDir, registry, WindowsFiles(AllWindowsFiles));

        Assert.True(report.CanRepair);
        Assert.All(report.Browsers, s => Assert.Equal(BrowserLinkState.Linked, s.State));
        Assert.Equal("All installed browsers are linked to this install.", report.Summary);
    }

    [Fact]
    public void Windows_a_missing_key_is_not_linked_and_the_summary_says_repair()
    {
        var report = NativeHostStatus.CheckWindows(AppDir, new FakeRegistry(), WindowsFiles(AllWindowsFiles));

        Assert.All(report.Browsers, s => Assert.Equal(BrowserLinkState.NotLinked, s.State));
        Assert.Contains("Repair", report.Summary);
    }

    [Fact]
    public void Windows_a_key_for_another_install_is_out_of_date_and_names_it()
    {
        var registry = new FakeRegistry();
        registry.Values[NativeHostRegistration.SubKeyFor(NativeHostRegistration.WindowsBrowsers[0])] = @"D:\other\native-host-manifest.json";

        var report = NativeHostStatus.CheckWindows(AppDir, registry, WindowsFiles(AllWindowsFiles));

        var first = report.Browsers[0];
        Assert.Equal(BrowserLinkState.OutOfDate, first.State);
        Assert.Contains(@"D:\other\native-host-manifest.json", first.Detail);
    }

    [Fact]
    public void Windows_without_the_shim_next_to_the_app_it_is_a_development_run()
    {
        var report = NativeHostStatus.CheckWindows(AppDir, new FakeRegistry(), WindowsFiles());

        Assert.False(report.CanRepair);
        Assert.Contains("Development run", report.Summary);
    }

    [Fact]
    public void Windows_a_registry_that_cannot_be_read_becomes_a_line_not_an_exception()
    {
        var report = NativeHostStatus.CheckWindows(AppDir, new FakeRegistry { Throws = true }, WindowsFiles(AllWindowsFiles));

        Assert.All(report.Browsers, s => Assert.Contains("access denied", s.Detail));
    }

    // ---- macOS and Linux ----

    [Fact]
    public void Unix_without_any_browser_profile_nothing_is_installed()
    {
        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, InstalledApp());

        Assert.All(report.Browsers, s => Assert.Equal(BrowserLinkState.NotInstalled, s.State));
        Assert.Equal("No supported browser was found.", report.Summary);
    }

    [Fact]
    public void Unix_an_installed_browser_without_a_manifest_is_not_linked()
    {
        var files = InstalledApp();
        files.Dirs.Add(NativeHostUnixRegistration.ProfileRoot(Chrome, HostOs.Linux, Home));

        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(BrowserLinkState.NotLinked, report.Browsers.First(s => s.Browser == "Chrome").State);
        Assert.Equal(BrowserLinkState.NotInstalled, report.Browsers.First(s => s.Browser == "Chromium").State);
        Assert.Contains("Repair", report.Summary);
    }

    [Fact]
    public void Unix_repair_then_check_gives_linked()
    {
        var files = InstalledApp();
        files.Dirs.Add(NativeHostUnixRegistration.ProfileRoot(Chrome, HostOs.Linux, Home));

        NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);
        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(BrowserLinkState.Linked, report.Browsers.First(s => s.Browser == "Chrome").State);
        Assert.Equal("All installed browsers are linked to this install.", report.Summary);
    }

    [Fact]
    public void Unix_a_manifest_for_another_shim_is_out_of_date()
    {
        var files = InstalledApp();
        files.Files[ChromeManifest] = NativeHostUnixRegistration.BuildManifestJson("/elsewhere/ytdlp-shim", new[] { Origin });

        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, files);

        var chrome = report.Browsers.First(s => s.Browser == "Chrome");
        Assert.Equal(BrowserLinkState.OutOfDate, chrome.State);
        Assert.Contains("/elsewhere/ytdlp-shim", chrome.Detail);
    }

    [Fact]
    public void Unix_the_same_shim_with_other_origins_is_out_of_date()
    {
        var files = InstalledApp();
        files.Files[ChromeManifest] = NativeHostUnixRegistration.BuildManifestJson(Shim, new[] { "chrome-extension://zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz/" });

        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(BrowserLinkState.OutOfDate, report.Browsers.First(s => s.Browser == "Chrome").State);
    }

    [Fact]
    public void Unix_an_unreadable_manifest_is_out_of_date()
    {
        var files = InstalledApp();
        files.Files[ChromeManifest] = "not json";

        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(BrowserLinkState.OutOfDate, report.Browsers.First(s => s.Browser == "Chrome").State);
    }

    [Fact]
    public void Unix_without_the_shim_it_is_a_development_run()
    {
        var report = NativeHostStatus.CheckUnix(HostOs.Linux, AppDir, Home, new FakeFiles());

        Assert.False(report.CanRepair);
        Assert.Contains("Development run", report.Summary);
    }
}
