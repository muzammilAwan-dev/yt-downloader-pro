using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using YTDLPHost.Native;
using YTDLPHost.Shim;

namespace YTDLPHost.Tests.Native;

/// <summary>
/// Phase 2, step 2.6: the files the installer ships (host/installer/native-host/) must agree with the extension's pinned id and with the code
/// that repairs the registration. A wrong id here is silent in the field: Chrome just refuses to start the host.
/// </summary>
public class NativeHostFilesTests
{
    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "extension", "manifest.json")) && Directory.Exists(Path.Combine(dir.FullName, "host", "installer")))
                return dir.FullName;
        }
        throw new InvalidOperationException("The repository root (extension/manifest.json and host/installer) was not found above " + AppContext.BaseDirectory);
    }

    private static string InstallerFile(params string[] parts) => Path.Combine(new[] { RepoRoot(), "host", "installer" }.Concat(parts).ToArray());

    /// <summary>Chrome's rule: the id is the first 16 bytes of the SHA-256 of the public key, written as hex with 0-f replaced by a-p.</summary>
    private static string PinnedOrigin()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "extension", "manifest.json")));
        byte[] publicKey = Convert.FromBase64String(manifest.RootElement.GetProperty("key").GetString()!);
        string hex = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant()[..32];
        string id = new(hex.Select(c => (char)('a' + Convert.ToInt32(c.ToString(), 16))).ToArray());
        return $"chrome-extension://{id}/";
    }

    [Fact]
    public void The_installed_manifest_names_the_host_and_the_shim_and_allows_only_the_pinned_extension()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(InstallerFile("native-host", NativeHostRegistration.ManifestFileName)));
        var root = doc.RootElement;

        Assert.Equal(NativeProtocol.HostName, root.GetProperty("name").GetString());
        Assert.Equal("stdio", root.GetProperty("type").GetString());
        Assert.Equal(NativeHostRegistration.ShimFileName, root.GetProperty("path").GetString());
        Assert.Equal(new[] { PinnedOrigin() }, root.GetProperty("allowed_origins").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public void The_installed_origins_file_lets_the_shim_accept_exactly_the_pinned_extension()
    {
        var allowed = ShimOrigin.ParseAllowed(File.ReadAllText(InstallerFile("native-host", NativeHostRegistration.AllowedOriginsFileName)));

        Assert.Equal(new[] { PinnedOrigin() }, allowed);
        Assert.True(ShimOrigin.IsAllowed(new[] { PinnedOrigin() }, allowed));
    }

    [Fact]
    public void The_installer_script_ships_the_three_files_and_writes_every_browser_key()
    {
        string iss = File.ReadAllText(InstallerFile("setup.iss"));

        foreach (string file in new[] { NativeHostRegistration.ShimFileName, NativeHostRegistration.ManifestFileName, NativeHostRegistration.AllowedOriginsFileName })
            Assert.Contains($"{file}\"; DestDir: \"{{app}}\"", iss);

        foreach (var browser in NativeHostRegistration.WindowsBrowsers)
            Assert.Contains($"Subkey: \"{NativeHostRegistration.SubKeyFor(browser)}\"", iss);

        Assert.Contains("ValueData: \"{app}\\" + NativeHostRegistration.ManifestFileName + "\"", iss);
    }
}
