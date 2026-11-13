using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Phase 3, step 3.2: decides whether an output or archive path that arrived over the ytdlp:// protocol may be written. A web page can craft that link, so the
    /// path is untrusted. The rules:
    /// absolute only (a drive letter, or a leading slash; "~" and "~/" mean the home folder); no "..", no UNC or \\?\ paths; the path is normalised
    /// the way Windows reads it (trailing dots and spaces dropped, 8.3 short names and ":" streams refused); every symbolic link and junction on the way
    /// is followed, and the real location is judged; and the result must stay out of system folders, the user's .ssh and .gnupg, and this app's own folders.
    /// All of it is text work on the path, whatever the running OS is, so both path styles are tested everywhere. Only the link lookup touches the disk.
    ///
    /// Not an allow-list of approved folders (decision 007): the extension's Save Directory and custom commands still send any folder over ytdlp://.
    /// When that route is removed, no path comes from a web page any more and the folder is the host setting.
    /// </summary>
    public sealed class PathPolicy
    {
        private const int MaxLinkHops = 32;

        // Lower-case, backslash separated; matched anywhere in the path (the same rules as before 3.2, plus the Unix and app folders).
        private static readonly string[] BlockedFragments =
        {
            @"\windows\", @"\system32", @"\program files", @"\programdata", @"\start menu", @"\startup", @"\appdata\roaming\microsoft",
            "ytdownloaderproengine", @"\.ssh\", @"\.gnupg\",
            @"\library\application support\yt downloader pro\", @"\library\logs\yt downloader pro\",
            @"\.local\share\yt-downloader-pro\", @"\.config\yt-downloader-pro\", @"\.local\state\yt-downloader-pro\"
        };

        // Unix system roots, only at the start of a Unix-style path.
        private static readonly string[] BlockedUnixRoots =
        {
            @"\etc", @"\bin", @"\sbin", @"\usr", @"\lib", @"\lib64", @"\proc", @"\sys", @"\boot", @"\dev", @"\root",
            @"\system", @"\library", @"\private\etc", @"\applications"
        };

        private static readonly Regex ShortName = new("~[0-9]", RegexOptions.Compiled);

        private readonly string _home;
        private readonly Func<string, string?> _resolveLink;

        /// <param name="home">What "~" stands for.</param>
        /// <param name="resolveLink">The final target of the symbolic link or junction at an absolute path, or null when it is none. Default: the real disk.</param>
        public PathPolicy(string home, Func<string, string?>? resolveLink = null)
        {
            _home = home ?? "";
            _resolveLink = resolveLink ?? FileSystemLinks.Resolve;
        }

        public static PathPolicy CreateDefault() => new(AppPaths.Current.HomeDir);

        public bool IsAllowed(string path)
        {
            if (!TryParse(path, true, out var p)) return false;

            for (int hops = 0; ; hops++)
            {
                int linkAt = -1;
                string? target = null;
                for (int i = 1; i <= p.Segments.Count; i++)
                {
                    target = _resolveLink(p.Join(i));
                    if (target != null) { linkAt = i; break; }
                }
                if (linkAt < 0) break;

                if (hops >= MaxLinkHops) return false;                       // a cycle of links
                if (!TryParse(target!, false, out var t)) return false;      // a link target that is not an absolute path
                p = new Parsed(t.Windows, t.Root, t.Segments.Concat(p.Segments.Skip(linkAt)).ToList());
            }

            return !IsBlocked(p);
        }

        private bool TryParse(string raw, bool expandHome, out Parsed parsed)
        {
            parsed = null!;
            if (string.IsNullOrEmpty(raw)) return false;
            foreach (char c in raw)
            {
                if (c < ' ') return false;
            }

            string s = raw;
            if (expandHome && s[0] == '~')
            {
                if (s.Length > 1 && s[1] != '/' && s[1] != '\\') return false;   // "~name" is not supported
                s = _home + s.Substring(1);
                if (s.Length == 0) return false;
            }

            bool windows;
            string root, rest;
            if (s.Length >= 3 && char.IsAsciiLetter(s[0]) && s[1] == ':' && (s[2] == '\\' || s[2] == '/'))
            {
                windows = true;
                root = s.Substring(0, 2);
                rest = s.Substring(3);
            }
            else if (s[0] == '/' || s[0] == '\\')
            {
                if (s.Length > 1 && (s[1] == '/' || s[1] == '\\')) return false;   // UNC and \\?\ paths
                windows = false;
                root = "";
                rest = s.Substring(1);
            }
            else
            {
                return false;   // relative, or "C:name"
            }

            var segments = new List<string>();
            foreach (string part in rest.Split('/', '\\'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") return false;

                string seg = windows ? part.TrimEnd('.', ' ') : part;   // Windows ignores trailing dots and spaces: "Windows." is "Windows"
                if (seg.Length == 0) return false;
                if (windows && (seg.Contains(':') || ShortName.IsMatch(seg))) return false;   // streams ("a.mp4:x") and 8.3 names ("PROGRA~1")
                segments.Add(seg);
            }
            if (segments.Count == 0) return false;

            parsed = new Parsed(windows, root, segments);
            return true;
        }

        private static bool IsBlocked(Parsed p)
        {
            string probe = "\\" + string.Join("\\", p.Segments).ToLowerInvariant() + "\\";

            foreach (var fragment in BlockedFragments)
            {
                if (probe.Contains(fragment, StringComparison.Ordinal)) return true;
            }
            if (!p.Windows)
            {
                foreach (var root in BlockedUnixRoots)
                {
                    if (probe.StartsWith(root + "\\", StringComparison.Ordinal)) return true;
                }
            }
            return false;
        }

        private sealed record Parsed(bool Windows, string Root, List<string> Segments)
        {
            /// <summary>The first <paramref name="count"/> segments as a path in the path's own style.</summary>
            public string Join(int count)
            {
                string sep = Windows ? "\\" : "/";
                return Root + sep + string.Join(sep, Segments.Take(count));
            }
        }
    }

    internal static class FileSystemLinks
    {
        /// <summary>The final target of the symbolic link or junction at <paramref name="path"/>, or null when it is none, does not exist or cannot be looked at.</summary>
        public static string? Resolve(string path)
        {
            try
            {
                bool windowsStyle = path.Length >= 2 && path[1] == ':';
                if (windowsStyle != OperatingSystem.IsWindows()) return null;   // a path in another OS's style cannot exist on this machine

                FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                return info.Exists ? info.ResolveLinkTarget(returnFinalTarget: true)?.FullName : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
