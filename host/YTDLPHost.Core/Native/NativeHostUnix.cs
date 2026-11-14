using System.Text;
using System.Text.Json;
using YTDLPHost.Services;

namespace YTDLPHost.Native
{
    /// <summary>A Chromium-family browser, with the folder of its profile data on macOS (under Application Support) and on Linux (under ~/.config).</summary>
    public sealed record UnixBrowser(string Name, string MacDir, string LinuxDir);

    /// <summary>The few file operations the Unix registrar needs; the disk in production, a fake in tests.</summary>
    public interface INativeHostFiles
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);

        /// <summary>The whole text of the file, or null when it does not exist.</summary>
        string? ReadAllText(string path);

        /// <summary>Creates missing folders and replaces the file in one step (readers never see half a manifest).</summary>
        void WriteAllText(string path, string content);

        void DeleteFile(string path);
    }

    public sealed class DiskNativeHostFiles : INativeHostFiles
    {
        public bool FileExists(string path) => File.Exists(path);

        public bool DirectoryExists(string path) => Directory.Exists(path);

        public string? ReadAllText(string path) => File.Exists(path) ? File.ReadAllText(path) : null;

        public void WriteAllText(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);   // 0644
            }
            File.Move(temp, path, overwrite: true);
        }

        public void DeleteFile(string path) => File.Delete(path);
    }

    /// <summary>
    /// Phase 3, step 3.4: Native Messaging registration on macOS and Linux. There is no registry: each browser reads
    /// <c>&lt;its profile folder&gt;/NativeMessagingHosts/com.ytdlppro.host.json</c>, and the manifest's "path" must be ABSOLUTE (only Windows allows a relative one).
    /// A browser counts as installed when its profile folder exists (a browser that never ran has none); its NativeMessagingHosts folder is created on demand.
    /// Same rules as the Windows side: nothing is done unless the shim and the origins file are next to the app, a stale manifest is rewritten, one browser's
    /// failure does not stop the others, and the returned lines are the log (see <see cref="NativeHostRegistration.Failed"/>).
    /// Firefox needs another manifest (allowed_extensions) and comes with Phase 6.
    /// </summary>
    public static class NativeHostUnixRegistration
    {
        /// <summary>Chrome and Chromium folders are in Chrome's own docs; Edge and Brave match what other installers write (corroborated, not yet seen on a real machine).</summary>
        public static readonly IReadOnlyList<UnixBrowser> Browsers = new[]
        {
            new UnixBrowser("Chrome", "Google/Chrome", "google-chrome"),
            new UnixBrowser("Chromium", "Chromium", "chromium"),
            new UnixBrowser("Edge", "Microsoft Edge", "microsoft-edge"),
            new UnixBrowser("Brave", "BraveSoftware/Brave-Browser", "BraveSoftware/Brave-Browser")
        };

        public static string ProfileRoot(UnixBrowser browser, HostOs os, string home) => os == HostOs.MacOS
            ? Path.Combine(home, "Library", "Application Support", Path.Combine(browser.MacDir.Split('/')))
            : Path.Combine(home, ".config", Path.Combine(browser.LinuxDir.Split('/')));

        public static string ManifestPath(UnixBrowser browser, HostOs os, string home) =>
            Path.Combine(ProfileRoot(browser, os, home), "NativeMessagingHosts", NativeProtocol.HostName + ".json");

        /// <summary>The manifest text. Built by hand, not with an indenting serializer, so it is identical on every OS (the idempotency check compares text).</summary>
        public static string BuildManifestJson(string shimPath, IEnumerable<string> allowedOrigins) =>
            "{\n" +
            "  \"name\": " + Json(NativeProtocol.HostName) + ",\n" +
            "  \"description\": \"YT Downloader Pro\",\n" +
            "  \"path\": " + Json(shimPath) + ",\n" +
            "  \"type\": \"stdio\",\n" +
            "  \"allowed_origins\": [" + string.Join(", ", allowedOrigins.Select(Json)) + "]\n" +
            "}\n";

        /// <summary>The "path" of a manifest, or null when the text is not a manifest object with one.</summary>
        public static string? TryGetManifestPath(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>The usable lines of allowed-origins.txt: "chrome-extension://&lt;id&gt;/", no wildcard (the browser rejects the whole manifest for anything else).</summary>
        public static IReadOnlyList<string> ParseOrigins(string text) => text
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("chrome-extension://", StringComparison.Ordinal) && line.EndsWith('/') && !line.Contains('*'))
            .ToList();

        public static IReadOnlyList<string> EnsureRegistered(HostOs os, string appDir, string home, INativeHostFiles files)
        {
            var log = new List<string>();
            if (os == HostOs.Windows)
            {
                log.Add("Skipped: Windows registers through the registry.");
                return log;
            }
            if (string.IsNullOrWhiteSpace(appDir) || !Path.IsPathRooted(appDir))
            {
                log.Add("Skipped: the app folder is unknown or not an absolute path.");
                return log;
            }

            string shimName = NativeHostRegistration.ShimFileNameFor(os);
            string shimPath = Path.Combine(appDir, shimName);
            string? originsText = files.ReadAllText(Path.Combine(appDir, NativeHostRegistration.AllowedOriginsFileName));
            if (!files.FileExists(shimPath) || originsText == null)
            {
                log.Add($"Skipped: {shimName} and {NativeHostRegistration.AllowedOriginsFileName} are not both next to the app (a development run, or an install without the browser link).");
                return log;
            }
            var origins = ParseOrigins(originsText);
            if (origins.Count == 0)
            {
                log.Add($"Skipped: {NativeHostRegistration.AllowedOriginsFileName} has no valid chrome-extension:// entry.");
                return log;
            }

            string manifest = BuildManifestJson(shimPath, origins);
            int found = 0;
            foreach (var browser in Browsers)
            {
                if (!files.DirectoryExists(ProfileRoot(browser, os, home)))
                {
                    log.Add($"{browser.Name}: not installed.");
                    continue;
                }

                found++;
                try
                {
                    string file = ManifestPath(browser, os, home);
                    if (files.ReadAllText(file) == manifest)
                    {
                        log.Add($"{browser.Name}: already registered.");
                        continue;
                    }
                    files.WriteAllText(file, manifest);
                    log.Add($"{browser.Name}: registered {file}.");
                }
                catch (Exception ex)
                {
                    log.Add($"{browser.Name}: could not register: {ex.Message}");
                }
            }

            if (found == 0) log.Add("Skipped: no supported browser found (looked for Chrome, Chromium, Edge and Brave profile folders).");
            return log;
        }

        /// <summary>Deletes the manifests whose "path" is the shim of this install; a manifest that points at another install is left alone.</summary>
        public static IReadOnlyList<string> Unregister(HostOs os, string appDir, string home, INativeHostFiles files)
        {
            var log = new List<string>();
            if (os == HostOs.Windows)
            {
                log.Add("Skipped: Windows registers through the registry.");
                return log;
            }
            if (string.IsNullOrWhiteSpace(appDir) || !Path.IsPathRooted(appDir))
            {
                log.Add("Skipped: the app folder is unknown or not an absolute path.");
                return log;
            }

            string shimPath = Path.Combine(appDir, NativeHostRegistration.ShimFileNameFor(os));
            foreach (var browser in Browsers)
            {
                try
                {
                    string file = ManifestPath(browser, os, home);
                    string? current = files.ReadAllText(file);
                    if (current == null)
                    {
                        log.Add($"{browser.Name}: not registered.");
                    }
                    else if (TryGetManifestPath(current) != shimPath)
                    {
                        log.Add($"{browser.Name}: left alone, it points to another install.");
                    }
                    else
                    {
                        files.DeleteFile(file);
                        log.Add($"{browser.Name}: unregistered.");
                    }
                }
                catch (Exception ex)
                {
                    log.Add($"{browser.Name}: could not unregister: {ex.Message}");
                }
            }
            return log;
        }

        private static string Json(string value) => JsonSerializer.Serialize(value);
    }
}
