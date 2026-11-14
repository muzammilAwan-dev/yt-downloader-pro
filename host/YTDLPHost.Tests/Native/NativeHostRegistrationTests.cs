using Xunit;
using YTDLPHost.Native;

namespace YTDLPHost.Tests.Native;

/// <summary>Phase 2, step 2.6: the app repairs the browsers' registration of the host manifest. A fake stands in for HKCU.</summary>
public class NativeHostRegistrationTests
{
    private static readonly string AppDir = Path.Combine("installed", "YT Downloader Pro");
    private static readonly string Manifest = Path.Combine(AppDir, NativeHostRegistration.ManifestFileName);

    private const string ChromeKey = @"Software\Google\Chrome\NativeMessagingHosts\com.ytdlppro.host";
    private const string EdgeKey = @"Software\Microsoft\Edge\NativeMessagingHosts\com.ytdlppro.host";
    private const string BraveKey = @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts\com.ytdlppro.host";

    private sealed class FakeRegistry : INativeHostRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Writes { get; } = new();
        public string? FailWhenKeyContains { get; set; }

        public string? GetDefaultValue(string subKey) => Values.TryGetValue(subKey, out var v) ? v : null;

        public List<string> Deletes { get; } = new();

        public void DeleteKey(string subKey)
        {
            Deletes.Add(subKey);
            Values.Remove(subKey);
        }

        public void SetDefaultValue(string subKey, string value)
        {
            if (FailWhenKeyContains != null && subKey.Contains(FailWhenKeyContains)) throw new UnauthorizedAccessException("access denied");
            Writes.Add(subKey);
            Values[subKey] = value;
        }
    }

    private static bool AllThere(string _) => true;

    [Fact]
    public void Chrome_edge_and_brave_are_pointed_at_the_manifest_next_to_the_app()
    {
        var registry = new FakeRegistry();

        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        Assert.Equal(3, registry.Writes.Count);
        Assert.Equal(Manifest, registry.Values[ChromeKey]);
        Assert.Equal(Manifest, registry.Values[EdgeKey]);
        Assert.Equal(Manifest, registry.Values[BraveKey]);
    }

    [Theory]
    [InlineData(NativeHostRegistration.ShimFileName)]
    [InlineData(NativeHostRegistration.ManifestFileName)]
    [InlineData(NativeHostRegistration.AllowedOriginsFileName)]
    public void Nothing_is_written_when_one_of_the_host_files_is_missing(string missing)
    {
        var registry = new FakeRegistry();
        registry.Values[ChromeKey] = "dev-registration";

        var log = NativeHostRegistration.EnsureRegistered(AppDir, registry, path => Path.GetFileName(path) != missing);

        Assert.Empty(registry.Writes);
        Assert.Equal("dev-registration", registry.Values[ChromeKey]);
        Assert.StartsWith("Skipped", Assert.Single(log));
    }

    [Fact]
    public void An_unknown_app_folder_is_skipped()
    {
        var registry = new FakeRegistry();

        var log = NativeHostRegistration.EnsureRegistered("", registry, AllThere);

        Assert.Empty(registry.Writes);
        Assert.StartsWith("Skipped", Assert.Single(log));
    }

    [Fact]
    public void A_second_run_writes_nothing()
    {
        var registry = new FakeRegistry();
        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);
        registry.Writes.Clear();

        var log = NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        Assert.Empty(registry.Writes);
        Assert.All(log, line => Assert.Contains("already registered", line));
    }

    [Fact]
    public void A_stale_value_is_repaired_and_only_that_browser_is_written()
    {
        var registry = new FakeRegistry();
        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);
        registry.Writes.Clear();
        registry.Values[EdgeKey] = @"E:\somewhere\else\native-host-manifest.json";

        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        Assert.Equal(new[] { EdgeKey }, registry.Writes);
        Assert.Equal(Manifest, registry.Values[EdgeKey]);
    }

    [Fact]
    public void A_path_that_differs_only_in_letter_case_counts_as_current()
    {
        var registry = new FakeRegistry();
        registry.Values[ChromeKey] = Manifest.ToUpperInvariant();

        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        Assert.DoesNotContain(ChromeKey, registry.Writes);
    }

    [Fact]
    public void A_browser_key_that_cannot_be_written_neither_stops_the_others_nor_throws()
    {
        var registry = new FakeRegistry { FailWhenKeyContains = "Edge" };

        var log = NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        Assert.Equal(new[] { ChromeKey, BraveKey }, registry.Writes);
        Assert.Contains(log, line => line.StartsWith("Edge: could not register"));
    }

    // ---------- unregister (step 3.4) ----------

    [Fact]
    public void Unregister_removes_the_keys_that_point_at_this_install()
    {
        var registry = new FakeRegistry();
        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        var log = NativeHostRegistration.Unregister(AppDir, registry);

        Assert.Empty(registry.Values);
        Assert.Equal(3, registry.Deletes.Count);
        Assert.False(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void Unregister_leaves_a_key_that_points_at_another_install_alone()
    {
        var registry = new FakeRegistry();
        registry.Values[ChromeKey] = @"E:\other\native-host-manifest.json";

        var log = NativeHostRegistration.Unregister(AppDir, registry);

        Assert.Empty(registry.Deletes);
        Assert.Equal(@"E:\other\native-host-manifest.json", registry.Values[ChromeKey]);
        Assert.Contains(log, line => line.StartsWith("Chrome: left alone"));
    }

    [Fact]
    public void Unregister_without_registrations_is_not_a_failure()
    {
        var log = NativeHostRegistration.Unregister(AppDir, new FakeRegistry());

        Assert.False(NativeHostRegistration.Failed(log));
    }

    [Fact]
    public void Unregister_reports_a_key_that_cannot_be_deleted_as_a_failure_and_goes_on()
    {
        var registry = new ThrowingDeleteRegistry();
        NativeHostRegistration.EnsureRegistered(AppDir, registry, AllThere);

        var log = NativeHostRegistration.Unregister(AppDir, registry);

        Assert.True(NativeHostRegistration.Failed(log));
        Assert.Single(registry.Values);   // only the key that refused is left
    }

    [Fact]
    public void An_unknown_app_folder_is_a_failure_for_both_directions()
    {
        Assert.True(NativeHostRegistration.Failed(NativeHostRegistration.Unregister("", new FakeRegistry())));
        Assert.True(NativeHostRegistration.Failed(NativeHostRegistration.EnsureRegistered("", new FakeRegistry(), AllThere)));
    }

    private sealed class ThrowingDeleteRegistry : INativeHostRegistry
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);

        public string? GetDefaultValue(string subKey) => Values.TryGetValue(subKey, out var v) ? v : null;

        public void SetDefaultValue(string subKey, string value) => Values[subKey] = value;

        public void DeleteKey(string subKey)
        {
            if (subKey.Contains("Edge")) throw new UnauthorizedAccessException("access denied");
            Values.Remove(subKey);
        }
    }
}
