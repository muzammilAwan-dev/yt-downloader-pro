using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Pure helpers for the live-speed readout and graph: no UI state, so they are easy to test and keep MainWindow's code-behind small.
    /// </summary>
    public static class RateMath
    {
        private static readonly Regex RatePattern =
            new(@"^\s*(?<n>[0-9]*\.?[0-9]+)\s*(?<u>[KMGT]?)i?B/s", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Turns yt-dlp's "3.30MiB/s" into bytes per second (0 when the text is not a rate).</summary>
        public static double ParseRate(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;

            var match = RatePattern.Match(text);
            if (!match.Success) return 0;
            if (!double.TryParse(match.Groups["n"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return 0;

            string prefix = match.Groups["u"].Value;
            double factor = prefix.Length == 0 ? 1d : char.ToUpperInvariant(prefix[0]) switch
            {
                'K' => 1024d,
                'M' => 1024d * 1024d,
                'G' => 1024d * 1024d * 1024d,
                'T' => 1024d * 1024d * 1024d * 1024d,
                _ => 1d
            };
            return value * factor;
        }

        /// <summary>Formats bytes per second as a number and a unit, e.g. ("3.30", "MiB/s").</summary>
        public static (string Value, string Unit) Format(double bytesPerSecond)
        {
            string[] units = { "B/s", "KiB/s", "MiB/s", "GiB/s" };
            int i = 0;
            while (bytesPerSecond >= 1024 && i < units.Length - 1)
            {
                bytesPerSecond /= 1024;
                i++;
            }

            string value = i == 0
                ? bytesPerSecond.ToString("0", CultureInfo.InvariantCulture)
                : bytesPerSecond.ToString("0.00", CultureInfo.InvariantCulture);
            return (value, units[i]);
        }
    }
}
