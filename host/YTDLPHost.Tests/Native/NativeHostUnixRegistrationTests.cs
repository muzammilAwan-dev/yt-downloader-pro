using System.Text.Json;
using Xunit;
using YTDLPHost.Native;
using YTDLPHost.Services;

namespace YTDLPHost.Tests.Native;

/// <summary>Phase 3, step 3.4: manifests for macOS and Linux browsers. An in-memory file system stands in for the disk, so it runs on every OS.</summary>
public class NativeHostUnixRegistrationTests
{
    private const string Origin = "chrome-extension://onlglcidnadbeilfhbahjodnhonhiieb/";

    private static readonly string Home = Path.Combine(Path.GetTempPath(), "unix-home");
    private static readonly string AppDir = Path.Combine(Path.GetTempPath(), "unix-app");
    private static readonly string Shim = Path.Combine(AppDir, "ytdlp-shim");

    private sealed class FakeFiles : INativeHostFiles
    {
        public Dictionary<string, string> Files { get; } = new();
        public HashSet<string> Dirs { get; } = new();
        public List<string> Writes { get; } = new();
        public List<string> Deleted { get; } = new();
        public string? FailWritesContaining { get; set; }

        public bool FileExists(string path) => Files.ContainsKey(path);

        public bool DirectoryExists(string path) => Dirs.Contains(path);

        public string? ReadAllText(string path) => Files.TryGetValue(path, out var text) ? text : null;

        public void WriteAllText(string path, string content)
        {
            if (FailWritesContaining != null && path.Contains(FailWritesContaining)) throw new IOException("disk full");
            Writes.Add(path);
            Files[path] = content;
        }

        public void DeleteFile(string path)
        {
            Deleted.Add(path);
            Files.Remove(path);
        }
    }

    /// <summary>An installed app (shim + origins file) and the given browsers' profile folders.</summary>
    private static FakeFiles Machine(HostOs os, params string[] browsers)
    {
        var files = new FakeFiles();
        files.Files[Shim] = "binary";
        files.Files[Path.Combine(AppDir, "allowed-origins.txt")] = Origin + "\n";
        foreach (string name in browsers)
        {
            var browser = NativeHostUnixRegistration.Browsers.Single(b => b.Name == name);
            files.Dirs.Add(NativeHostUnixRegistration.ProfileRoot(browser, os, Home));
        }
        return files;
    }

    private static string ManifestOf(string browserName, HostOs os) =>
        NativeHostUnixRegistration.ManifestPath(NativeHostUnixRegistration.Browsers.Single(b => b.Name == browserName), os, Home);

    [Fact]
    public void Linux_manifests_go_to_the_config_folder_of_each_installed_browser_only()
    {
        var files = Machine(HostOs.Linux, "Chrome", "Brave");

        var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(Path.Combine(Home, ".config", "google-chrome", "NativeMessagingHosts", "com.ytdlppro.host.json"), ManifestOf("Chrome", HostOs.Linux));
        Assert.Equal(Path.Combine(Home, ".config", "BraveSoftware", "Brave-Browser", "NativeMessagingHosts", "com.ytdlppro.host.json"), ManifestOf("Brave", HostOs.Linux));
        Assert.Equal(new[] { ManifestOf("Chrome", HostOs.Linux), ManifestOf("Brave", HostOs.Linux) }, files.Writes);
        Assert.Contains(log, line => line == "Edge: not installed.");
        Assert.False(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void MacOS_manifests_go_to_Application_Support()
    {
        var files = Machine(HostOs.MacOS, "Chrome", "Edge");

        NativeHostUnixRegistration.EnsureRegistered(HostOs.MacOS, AppDir, Home, files);

        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "Google", "Chrome", "NativeMessagingHosts", "com.ytdlppro.host.json"), ManifestOf("Chrome", HostOs.MacOS));
        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "Microsoft Edge", "NativeMessagingHosts", "com.ytdlppro.host.json"), ManifestOf("Edge", HostOs.MacOS));
        Assert.Equal(new[] { ManifestOf("Chrome", HostOs.MacOS), ManifestOf("Edge", HostOs.MacOS) }, files.Writes);
    }

    [Fact]
    public void The_manifest_names_the_absolute_shim_and_the_origins_from_the_file()
    {
        var files = Machine(HostOs.Linux, "Chromium");

        NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        using var doc = JsonDocument.Parse(files.Files[ManifestOf("Chromium", HostOs.Linux)]);
        var root = doc.RootElement;
        Assert.Equal(NativeProtocol.HostName, root.GetProperty("name").GetString());
        Assert.Equal("stdio", root.GetProperty("type").GetString());
        Assert.Equal(Shim, root.GetProperty("path").GetString());
        Assert.True(Path.IsPathRooted(root.GetProperty("path").GetString()!));
        Assert.Equal(new[] { Origin }, root.GetProperty("allowed_origins").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void A_path_with_backslashes_and_quotes_survives_the_json_round_trip()
    {
        string awkward = "C:\\apps\\\"odd\" name\\ytdlp-shim";

        string json = NativeHostUnixRegistration.BuildManifestJson(awkward, new[] { Origin });

        Assert.Equal(awkward, NativeHostUnixRegistration.TryGetManifestPath(json));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"name\": \"x\"}")]
    [InlineData("{\"path\": 5}")]
    public void A_text_that_is_not_a_manifest_has_no_path(string text)
    {
        Assert.Null(NativeHostUnixRegistration.TryGetManifestPath(text));
    }

    [Fact]
    public void Only_well_formed_origins_are_used()
    {
        string text = "# comment\r\n" + Origin + "\r\n\r\nchrome-extension://*/\r\nhttps://example.com/\r\nchrome-extension://nosuffix\r\n";

        Assert.Equal(new[] { Origin }, NativeHostUnixRegistration.ParseOrigins(text));
    }

    [Fact]
    public void Windows_is_skipped()
    {
        var files = Machine(HostOs.Linux, "Chrome");

        var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Windows, AppDir, Home, files);

        Assert.Empty(files.Writes);
        Assert.True(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void A_relative_or_empty_app_folder_is_skipped()
    {
        foreach (string dir in new[] { "", "relative/app" })
        {
            var files = Machine(HostOs.Linux, "Chrome");

            var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, dir, Home, files);

            Assert.Empty(files.Writes);
            Assert.True(NativeHostRegistration.Failed(log));
        }
    }

    [Fact]
    public void Nothing_is_written_without_the_shim()
    {
        var files = Machine(HostOs.Linux, "Chrome");
        files.Files.Remove(Shim);

        var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        Assert.Empty(files.Writes);
        Assert.True(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void Nothing_is_written_without_a_usable_origins_file()
    {
        var withoutFile = Machine(HostOs.Linux, "Chrome");
        withoutFile.Files.Remove(Path.Combine(AppDir, "allowed-origins.txt"));
        var withoutEntries = Machine(HostOs.Linux, "Chrome");
        withoutEntries.Files[Path.Combine(AppDir, "allowed-origins.txt")] = "https://example.com/\n";

        Assert.True(NativeHostRegistration.Failed(NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, withoutFile)));
        Assert.True(NativeHostRegistration.Failed(NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, withoutEntries)));
        Assert.Empty(withoutFile.Writes);
        Assert.Empty(withoutEntries.Writes);
    }

    [Fact]
    public void No_installed_browser_is_reported_as_a_failure()
    {
        var files = Machine(HostOs.Linux);

        var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        Assert.Empty(files.Writes);
        Assert.True(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void A_second_run_writes_nothing()
    {
        var files = Machine(HostOs.Linux, "Chrome", "Edge");
        NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);
        files.Writes.Clear();

        var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        Assert.Empty(files.Writes);
        Assert.Contains(log, line => line == "Chrome: already registered.");
    }

    [Fact]
    public void A_stale_manifest_is_rewritten()
    {
        var files = Machine(HostOs.Linux, "Chrome");
        files.Files[ManifestOf("Chrome", HostOs.Linux)] = "{ \"path\": \"/old/place/ytdlp-shim\" }";

        NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(Shim, NativeHostUnixRegistration.TryGetManifestPath(files.Files[ManifestOf("Chrome", HostOs.Linux)]));
    }

    [Fact]
    public void A_browser_that_cannot_be_written_neither_stops_the_others_nor_throws()
    {
        var files = Machine(HostOs.Linux, "Chrome", "Edge", "Brave");
        files.FailWritesContaining = "microsoft-edge";

        var log = NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(new[] { ManifestOf("Chrome", HostOs.Linux), ManifestOf("Brave", HostOs.Linux) }, files.Writes);
        Assert.Contains(log, line => line.StartsWith("Edge: could not register"));
        Assert.True(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void Unregister_deletes_only_manifests_that_point_at_this_install()
    {
        var files = Machine(HostOs.Linux, "Chrome", "Edge", "Brave");
        NativeHostUnixRegistration.EnsureRegistered(HostOs.Linux, AppDir, Home, files);
        files.Files[ManifestOf("Edge", HostOs.Linux)] = NativeHostUnixRegistration.BuildManifestJson("/somewhere/else/ytdlp-shim", new[] { Origin });
        files.Files.Remove(ManifestOf("Brave", HostOs.Linux));

        var log = NativeHostUnixRegistration.Unregister(HostOs.Linux, AppDir, Home, files);

        Assert.Equal(new[] { ManifestOf("Chrome", HostOs.Linux) }, files.Deleted);
        Assert.True(files.Files.ContainsKey(ManifestOf("Edge", HostOs.Linux)));
        Assert.Contains(log, line => line.StartsWith("Edge: left alone"));
        Assert.Contains(log, line => line == "Brave: not registered.");
        Assert.False(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void The_disk_implementation_writes_atomically_creates_folders_and_deletes()
    {
        string root = Path.Combine(Path.GetTempPath(), "nmh-disk-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var disk = new DiskNativeHostFiles();
            string file = Path.Combine(root, "a", "b", "com.ytdlppro.host.json");

            Assert.Null(disk.ReadAllText(file));
            disk.WriteAllText(file, "one");
            disk.WriteAllText(file, "two");

            Assert.Equal("two", disk.ReadAllText(file));
            Assert.True(disk.FileExists(file));
            Assert.True(disk.DirectoryExists(Path.Combine(root, "a", "b")));
            Assert.False(File.Exists(file + ".tmp"));
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, File.GetUnixFileMode(file));
            }

            disk.DeleteFile(file);
            Assert.Null(disk.ReadAllText(file));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void The_shim_is_called_ytdlp_shim_exe_on_Windows_and_ytdlp_shim_elsewhere()
    {
        Assert.Equal("ytdlp-shim.exe", NativeHostRegistration.ShimFileNameFor(HostOs.Windows));
        Assert.Equal("ytdlp-shim", NativeHostRegistration.ShimFileNameFor(HostOs.Linux));
        Assert.Equal("ytdlp-shim", NativeHostRegistration.ShimFileNameFor(HostOs.MacOS));
    }
}
