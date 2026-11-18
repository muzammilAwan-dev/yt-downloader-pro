using YTDLPHost.Services;

namespace YTDLPHost.Native
{
    public enum BrowserLinkState
    {
        /// <summary>The browser starts this install's shim.</summary>
        Linked,
        /// <summary>No registration for this browser.</summary>
        NotLinked,
        /// <summary>A registration exists but points at another install, or its content is stale. Repair rewrites it.</summary>
        OutOfDate,
        /// <summary>macOS and Linux only: the browser's profile folder does not exist.</summary>
        NotInstalled
    }

    public sealed record BrowserLinkStatus(string Browser, BrowserLinkState State, string Detail);

    /// <param name="CanRepair">False when the shim, the manifest or the origins file are not next to the app (a development run): Repair would do nothing.</param>
    /// <param name="Summary">One sentence for the Browser integration panel.</param>
    public sealed record NativeHostReport(bool CanRepair, string Summary, IReadOnlyList<BrowserLinkStatus> Browsers);

    /// <summary>
    /// Step 4.4: what the Browser integration panel shows. Read-only counterpart of <see cref="NativeHostRegistration"/> (Windows registry) and
    /// <see cref="NativeHostUnixRegistration"/> (manifest files): the same rules decide what "linked" means, and Repair is simply their EnsureRegistered.
    /// Never throws: a registry or a folder that cannot be read becomes a line of the report.
    /// </summary>
    public static class NativeHostStatus
    {
        public static NativeHostReport CheckWindows(string appDir, INativeHostRegistry registry, Func<string, bool> fileExists)
        {
            bool canRepair = !string.IsNullOrWhiteSpace(appDir) && new[]
            {
                NativeHostRegistration.ShimFileName, NativeHostRegistration.AllowedOriginsFileName, NativeHostRegistration.ManifestFileName
            }.All(name => fileExists(Path.Combine(appDir, name)));

            string manifestPath = Path.Combine(appDir ?? "", NativeHostRegistration.ManifestFileName);
            var list = new List<BrowserLinkStatus>();
            foreach (var browser in NativeHostRegistration.WindowsBrowsers)
            {
                try
                {
                    string? current = registry.GetDefaultValue(NativeHostRegistration.SubKeyFor(browser));
                    if (current == null) list.Add(new(browser.Name, BrowserLinkState.NotLinked, "Not linked."));
                    else if (string.Equals(current, manifestPath, StringComparison.OrdinalIgnoreCase)) list.Add(new(browser.Name, BrowserLinkState.Linked, "Linked to this install."));
                    else list.Add(new(browser.Name, BrowserLinkState.OutOfDate, $"Points to another install: {current}"));
                }
                catch (Exception ex)
                {
                    list.Add(new(browser.Name, BrowserLinkState.NotLinked, $"Could not read the registration: {ex.Message}"));
                }
            }
            return new NativeHostReport(canRepair, Summarize(canRepair, list), list);
        }

        public static NativeHostReport CheckUnix(HostOs os, string appDir, string home, INativeHostFiles files)
        {
            bool rooted = !string.IsNullOrWhiteSpace(appDir) && Path.IsPathRooted(appDir);
            string shimPath = rooted ? Path.Combine(appDir, NativeHostRegistration.ShimFileNameFor(os)) : "";
            string? originsText = rooted ? SafeRead(files, Path.Combine(appDir, NativeHostRegistration.AllowedOriginsFileName)) : null;
            var origins = originsText == null ? new List<string>() : NativeHostUnixRegistration.ParseOrigins(originsText).ToList();
            bool canRepair = rooted && files.FileExists(shimPath) && origins.Count > 0;
            string? expected = canRepair ? NativeHostUnixRegistration.BuildManifestJson(shimPath, origins) : null;

            var list = new List<BrowserLinkStatus>();
            foreach (var browser in NativeHostUnixRegistration.Browsers)
            {
                try
                {
                    if (!files.DirectoryExists(NativeHostUnixRegistration.ProfileRoot(browser, os, home)))
                    {
                        list.Add(new(browser.Name, BrowserLinkState.NotInstalled, "Not installed."));
                        continue;
                    }

                    string? current = files.ReadAllText(NativeHostUnixRegistration.ManifestPath(browser, os, home));
                    if (current == null)
                    {
                        list.Add(new(browser.Name, BrowserLinkState.NotLinked, "Not linked."));
                        continue;
                    }

                    string? currentShim = NativeHostUnixRegistration.TryGetManifestPath(current);
                    if (expected != null && current == expected) list.Add(new(browser.Name, BrowserLinkState.Linked, "Linked to this install."));
                    else if (expected == null && currentShim == shimPath) list.Add(new(browser.Name, BrowserLinkState.Linked, "Linked to this install."));
                    else if (currentShim == shimPath) list.Add(new(browser.Name, BrowserLinkState.OutOfDate, "The link is out of date (its content differs)."));
                    else if (currentShim == null) list.Add(new(browser.Name, BrowserLinkState.OutOfDate, "The registration file cannot be read."));
                    else list.Add(new(browser.Name, BrowserLinkState.OutOfDate, $"Points to another install: {currentShim}"));
                }
                catch (Exception ex)
                {
                    list.Add(new(browser.Name, BrowserLinkState.NotLinked, $"Could not read the registration: {ex.Message}"));
                }
            }
            return new NativeHostReport(canRepair, Summarize(canRepair, list), list);
        }

        private static string? SafeRead(INativeHostFiles files, string path)
        {
            try { return files.ReadAllText(path); }
            catch { return null; }
        }

        private static string Summarize(bool canRepair, IReadOnlyList<BrowserLinkStatus> list)
        {
            if (!canRepair) return "Development run: the browser shim is not next to the app, so the links cannot be repaired from here.";
            if (list.All(s => s.State == BrowserLinkState.NotInstalled)) return "No supported browser was found.";
            if (list.Any(s => s.State is BrowserLinkState.NotLinked or BrowserLinkState.OutOfDate)) return "Some browsers are not linked to this install. Press Repair.";
            return "All installed browsers are linked to this install.";
        }
    }
}
