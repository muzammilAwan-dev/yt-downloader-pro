using YTDLPHost.Services;

namespace YTDLPHost.Native
{
    /// <summary>One browser's per-user place for Native Messaging host registrations (a registry key on Windows).</summary>
    /// <param name="KeyBase">The key that holds one sub key per host name, relative to HKCU.</param>
    public sealed record NativeHostBrowser(string Name, string KeyBase);

    /// <summary>The per-user registry values <see cref="NativeHostRegistration"/> reads and writes: HKCU on Windows, a fake in tests.</summary>
    public interface INativeHostRegistry
    {
        /// <summary>The default value of the key, or null when the key or the value does not exist.</summary>
        string? GetDefaultValue(string subKey);

        /// <summary>Creates the key if needed and sets its default value.</summary>
        void SetDefaultValue(string subKey, string value);

        /// <summary>Deletes the key and everything below it; nothing happens when it does not exist.</summary>
        void DeleteKey(string subKey);
    }

    /// <summary>
    /// Step 2.6: how the browser finds the shim. Chrome, Edge and Brave read HKCU\Software\&lt;browser&gt;\NativeMessagingHosts\com.ytdlppro.host;
    /// its default value is the path of the host manifest, which names the shim and the one extension that may start it.
    /// The installer ships the three files below into the app folder and writes the values; the app repairs them at every start
    /// (a different user than the one who ran the installer, an app folder that was moved, a dev registration that points elsewhere).
    /// </summary>
    public static class NativeHostRegistration
    {
        public const string ShimFileName = "ytdlp-shim.exe";
        public const string ManifestFileName = "native-host-manifest.json";
        public const string AllowedOriginsFileName = "allowed-origins.txt";

        /// <summary>The shim's file name: ".exe" on Windows only. The shim looks for the app the same way (see the shim's StartApp).</summary>
        public static string ShimFileNameFor(HostOs os) => os == HostOs.Windows ? ShimFileName : "ytdlp-shim";

        /// <summary>
        /// Did a register or unregister run go wrong? True for a line that starts with "Skipped" or contains ": could not ". These words are the contract
        /// of the log lines of this class and of <see cref="NativeHostUnixRegistration"/>; the tests pin them. Used for the exit code of the command line flags.
        /// </summary>
        public static bool Failed(IEnumerable<string> log) =>
            log.Any(line => line.StartsWith("Skipped", StringComparison.Ordinal) || line.Contains(": could not ", StringComparison.Ordinal));

        /// <summary>
        /// Removes the browser keys that point at the manifest of this install (<c>--unregister-native-host</c>). A key that points at another install is left alone.
        /// </summary>
        public static IReadOnlyList<string> Unregister(string appDir, INativeHostRegistry registry)
        {
            var log = new List<string>();
            if (string.IsNullOrWhiteSpace(appDir))
            {
                log.Add("Skipped: the app folder is unknown.");
                return log;
            }

            string manifestPath = Path.Combine(appDir, ManifestFileName);
            foreach (var browser in WindowsBrowsers)
            {
                try
                {
                    string subKey = SubKeyFor(browser);
                    string? current = registry.GetDefaultValue(subKey);
                    if (current == null)
                    {
                        log.Add($"{browser.Name}: not registered.");
                    }
                    else if (!string.Equals(current, manifestPath, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Add($"{browser.Name}: left alone, it points to another install ({current}).");
                    }
                    else
                    {
                        registry.DeleteKey(subKey);
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

        /// <summary>Per-user registration keys on Windows (the macOS and Linux file locations come with Phase 3). Firefox needs another manifest format (Phase 6).</summary>
        public static readonly IReadOnlyList<NativeHostBrowser> WindowsBrowsers = new[]
        {
            new NativeHostBrowser("Chrome", @"Software\Google\Chrome\NativeMessagingHosts"),
            new NativeHostBrowser("Edge", @"Software\Microsoft\Edge\NativeMessagingHosts"),
            new NativeHostBrowser("Brave", @"Software\BraveSoftware\Brave-Browser\NativeMessagingHosts")
        };

        public static string SubKeyFor(NativeHostBrowser browser) => browser.KeyBase + "\\" + NativeProtocol.HostName;

        /// <summary>
        /// Points every browser key at the manifest in <paramref name="appDir"/>, but only when the shim, the manifest and the origins file are all
        /// there: a development run (no shim next to the exe) leaves whatever registration exists, such as the one of tools/register-native-host-dev.ps1.
        /// Never throws: a registry that refuses a write must not stop the app from starting. Returns what happened, one line per decision, for the log.
        /// </summary>
        public static IReadOnlyList<string> EnsureRegistered(string appDir, INativeHostRegistry registry, Func<string, bool> fileExists)
        {
            var log = new List<string>();
            if (string.IsNullOrWhiteSpace(appDir))
            {
                log.Add("Skipped: the app folder is unknown.");
                return log;
            }

            foreach (string name in new[] { ShimFileName, AllowedOriginsFileName, ManifestFileName })
            {
                if (!fileExists(Path.Combine(appDir, name)))
                {
                    log.Add($"Skipped: {name} is not next to the app (a development run, or an install without the browser link).");
                    return log;
                }
            }

            string manifestPath = Path.Combine(appDir, ManifestFileName);
            foreach (var browser in WindowsBrowsers)
            {
                try
                {
                    string subKey = SubKeyFor(browser);
                    string? current = registry.GetDefaultValue(subKey);
                    if (string.Equals(current, manifestPath, StringComparison.OrdinalIgnoreCase))
                    {
                        log.Add($"{browser.Name}: already registered.");
                        continue;
                    }

                    registry.SetDefaultValue(subKey, manifestPath);
                    log.Add($"{browser.Name}: registered {manifestPath}" + (current == null ? "." : $" (was {current})."));
                }
                catch (Exception ex)
                {
                    log.Add($"{browser.Name}: could not register: {ex.Message}");
                }
            }
            return log;
        }
    }
}
