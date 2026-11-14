using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>A downloaded engine file did not pass its integrity check (wrong checksum, or none published for it). Nothing was installed.</summary>
    public sealed class EngineVerificationException : Exception
    {
        public EngineVerificationException(string message) : base(message) { }
    }

    public static class Sha256Hex
    {
        public static string Of(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    /// <summary>
    /// Phase 3, step 3.5: reads the checksum files the engine projects publish next to their release files:
    /// yt-dlp's SHA2-256SUMS and BtbN's checksums.sha256 (GNU style, "hash  name", one line per file, name possibly with a "*" or a path in front), and
    /// Deno's per-file .sha256sum (GNU style on most platforms; the Windows one is bigger and looks like PowerShell Get-FileHash output, so the reading is
    /// tolerant: a line counts when it holds a 64-digit hash and ends with the file name). A per-file checksum file may also be nothing but the hash.
    /// </summary>
    public static class Sha256Sums
    {
        private static readonly Regex HashToken = new("(?<![0-9a-fA-F])[0-9a-fA-F]{64}(?![0-9a-fA-F])", RegexOptions.Compiled);

        /// <returns>The lower-case hash of <paramref name="fileName"/>, or null when the text has none for it.</returns>
        /// <param name="allowBare">Accept a text that holds exactly one hash and no file name (a per-file checksum file). Never for a list of many files.</param>
        public static string? FindHash(string text, string fileName, bool allowBare = false)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                var match = HashToken.Match(line);
                if (!match.Success) continue;

                string hash = match.Value.ToLowerInvariant();
                seen.Add(hash);
                string rest = line.Remove(match.Index, match.Length).Trim(' ', '\t', '*').Replace('\\', '/');
                if (EndsWithName(rest, fileName)) return hash;
            }
            return allowBare && seen.Count == 1 ? seen.First() : null;
        }

        private static bool EndsWithName(string rest, string fileName)
        {
            if (fileName.Length == 0 || !rest.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) return false;
            int before = rest.Length - fileName.Length - 1;
            return before < 0 || rest[before] == '/' || rest[before] == ' ' || rest[before] == '*';   // "my-yt-dlp.exe" is another file
        }
    }
}
