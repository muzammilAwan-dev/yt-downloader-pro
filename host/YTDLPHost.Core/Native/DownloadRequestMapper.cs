using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YTDLPHost.Services;

namespace YTDLPHost.Native
{
    /// <param name="DownloadFolder">The user's download folder from the host settings; empty = the default folder.</param>
    public sealed record MapperSettings(string DownloadFolder);

    /// <summary>One download ready for the queue.</summary>
    /// <param name="Arguments">The yt-dlp arguments, without the program name and without the engine paths (the runner adds those).</param>
    /// <param name="Command">The same arguments as the quoted "yt-dlp ..." string the rest of the app stores in the task and history.
    /// It always tokenizes back to <paramref name="Arguments"/> and has already passed <see cref="CommandValidator"/>.</param>
    public sealed record MappedDownload(IReadOnlyList<string> Arguments, string Command, string? CookiePayload, string? UserAgent);

    public sealed record MapResult(IReadOnlyList<MappedDownload> Downloads, string? Error)
    {
        public bool Ok => Error == null;
    }

    /// <summary>
    /// Turns a validated browser request into the yt-dlp arguments (Phase 2, step 2.2; ADR-003: the HOST builds the command, the
    /// extension only states choices). The rules reproduce what the extension builds today (content.js launchDownload and
    /// handleChannelDownload), which the golden tests check case by case. The extension has already applied per-site gating, so the
    /// options in the request are the EFFECTIVE choices; this class knows nothing about individual sites.
    /// The save folder always comes from <see cref="MapperSettings"/>, never from the request. Every command is checked once more by
    /// <see cref="CommandValidator"/> before it is returned, so a bug here cannot produce a command the app would refuse elsewhere.
    /// </summary>
    public static class DownloadRequestMapper
    {
        public const string DefaultFolder = "~/Downloads/YTDownloaderPro";

        // yt-dlp's own template language: "%(title)s" is a field and "%%" a literal percent sign.
        private const string TitleField = "%(title)s";

        private static readonly string[] AlwaysQuotedAfter = { "-f", "-S", "--download-sections", "--sub-langs", "--playlist-items", "-o", "--download-archive" };
        private static readonly Regex TikTokVideoId = new(@"/video/(\d{17,20})", RegexOptions.Compiled);

        public static MapResult Map(DownloadRequest request, MapperSettings settings)
        {
            try
            {
                string folder = NormalizeFolder(settings.DownloadFolder);
                var options = request.Options;
                var downloads = new List<MappedDownload>();

                if (options.Job == "channel")
                {
                    downloads.Add(Finish(ChannelArguments(request, folder), request.Auth));
                }
                else
                {
                    foreach (var url in request.Urls)
                    {
                        var args = options.Quality == "custom" ? CustomArguments(options.Custom!, url) : VideoArguments(options, url, folder);
                        downloads.Add(Finish(args, request.Auth));
                    }
                }

                return new MapResult(downloads, null);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return new MapResult(Array.Empty<MappedDownload>(), ex.Message);
            }
        }

        // ------------------------------------------------------------------ one video (or one playlist link)

        private static List<string> VideoArguments(DownloadOptions o, string url, string folder)
        {
            bool audio = o.Quality == "audio";
            string resLabel = audio ? "Audio" : $"{o.Quality}p";

            string format;
            string ext;
            var formatOptions = new List<string>();
            if (audio)
            {
                format = "ba";
                ext = o.AudioFormat;
                formatOptions.AddRange(new[] { "--extract-audio", "--audio-format", o.AudioFormat });
                if (o.AudioFormat != "flac" && o.AudioFormat != "wav") formatOptions.AddRange(new[] { "--audio-quality", "0" });
            }
            else
            {
                format = $"bv*[height<={o.Quality}]+ba/b[height<={o.Quality}]/bv*+ba/b";
                bool tall = int.Parse(o.Quality, CultureInfo.InvariantCulture) >= 1440; // 1440 and 2160 are muxed to mkv
                ext = tall ? "mkv" : "mp4";
                formatOptions.AddRange(new[] { "--merge-output-format", ext });
            }

            var args = new List<string> { "-f", format };
            if (o.Compat) args.AddRange(new[] { "-S", "vcodec:h264,res,acodec:m4a" });
            args.AddRange(formatOptions);
            args.AddRange(new[] { "-N", o.ConcurrentFragments.ToString(CultureInfo.InvariantCulture) });

            if (o.Trim != null)
            {
                args.AddRange(new[] { "--download-sections", $"*{o.Trim.Start ?? "00:00:00"}-{o.Trim.End ?? "inf"}" });
                if (o.CropKeyframeCuts) args.Add("--force-keyframes-at-cuts");
            }

            if (o.Subs && !audio)
                args.AddRange(new[] { "--write-subs", "--write-auto-subs", "--embed-subs", "--sub-langs", "en.*", "--sleep-subtitles", "5" });

            if (o.Playlist)
            {
                args.Add("--yes-playlist");
                if (o.PlaylistItems != null) args.AddRange(new[] { "--playlist-items", o.PlaylistItems });
            }
            else
            {
                args.Add("--no-playlist");
            }

            if (o.SponsorBlock) args.AddRange(new[] { "--sponsorblock-remove", "all" });
            args.Add("--restrict-filenames");
            if (o.EmbedMetadata) args.Add("--embed-metadata");
            if (o.EmbedThumbnail && ext != "wav") args.Add("--embed-thumbnail");
            args.AddRange(new[] { "--no-warnings", "--progress" });

            string tag = NameTag(o, audio);
            string template = o.Playlist
                ? $"%(playlist_title)s/%(playlist_index)03d_{TitleField}{tag}_{resLabel}.{ext}"
                : $"{TitleField}{UniqueSuffix(o, url)}{tag}_{resLabel}.{ext}";
            args.AddRange(new[] { "-o", Join(folder, template), url });
            return args;
        }

        // ------------------------------------------------------------------ channel / profile job

        private static List<string> ChannelArguments(DownloadRequest request, string folder)
        {
            var o = request.Options;
            bool audio = o.Quality == "audio";
            string resLabel = audio ? "Audio" : $"{o.Quality}p";

            // Same as the extension: a channel job always muxes to mp4 and has no audio-quality flag.
            string format = audio ? "ba" : $"bv*[height<={o.Quality}]+ba/b[height<={o.Quality}]/bv*+ba/b";
            string ext = audio ? o.AudioFormat : "mp4";

            var args = new List<string> { "--yes-playlist", "--ignore-errors", "-f", format };
            if (o.Compat) args.AddRange(new[] { "-S", "vcodec:h264,res,acodec:m4a" });
            if (audio) args.AddRange(new[] { "--extract-audio", "--audio-format", o.AudioFormat });
            else args.AddRange(new[] { "--merge-output-format", "mp4" });
            args.AddRange(new[] { "-N", o.ConcurrentFragments.ToString(CultureInfo.InvariantCulture) });
            args.AddRange(new[] { "--sleep-requests", "1", "--sleep-interval", "5", "--max-sleep-interval", "15" });
            args.AddRange(new[] { "--download-archive", Join(folder, $"{o.ArchiveName}.archive.txt") });
            args.Add("--restrict-filenames");
            if (o.EmbedMetadata) args.Add("--embed-metadata");
            if (o.EmbedThumbnail && ext != "wav") args.Add("--embed-thumbnail");
            args.AddRange(new[] { "--no-warnings", "--progress" });

            string nameTemplate = o.UniqueFilename ? $"{TitleField}_%(id)s" : TitleField;
            args.AddRange(new[] { "-o", Join(folder, $"%(channel,uploader)s/{nameTemplate}_{resLabel}.{ext}") });
            args.AddRange(request.Urls);
            return args;
        }

        // ------------------------------------------------------------------ custom command

        private static List<string> CustomArguments(string custom, string url)
        {
            var args = CommandTokenizer.Tokenize(custom) ?? throw new FormatException("The custom command has an unclosed quote.");
            if (args.Count > 0 && (args[0].Equals("yt-dlp", StringComparison.OrdinalIgnoreCase) || args[0].Equals("yt-dlp.exe", StringComparison.OrdinalIgnoreCase)))
                args.RemoveAt(0);
            if (!args.Contains(url)) args.Add(url); // like the extension: add the page link unless the command already has it
            return args;
        }

        // ------------------------------------------------------------------ names and folders

        /// <summary>Marks results that differ from the plain download so yt-dlp does not skip them as "already downloaded". Same rule as ytdlpNameTag in sites.js.</summary>
        private static string NameTag(DownloadOptions o, bool audio)
        {
            var tag = new StringBuilder();
            if (o.Trim != null) tag.Append("_clip_").Append(TagPart(o.Trim.Start ?? "00:00:00")).Append("_to_").Append(TagPart(o.Trim.End ?? "end"));
            if (o.SponsorBlock) tag.Append("_nosponsors");
            if (o.Compat && !audio) tag.Append("_h264");
            return tag.ToString();
        }

        private static string TagPart(string time) => Regex.Replace(time, "[^0-9A-Za-z]+", "-").Trim('-');

        private static string UniqueSuffix(DownloadOptions o, string url)
        {
            if (!o.UniqueFilename) return "";
            var match = TikTokVideoId.Match(url);
            return "_" + (match.Success ? match.Groups[1].Value : "%(id)s");
        }

        /// <summary>
        /// Forward slashes (yt-dlp accepts them on Windows, and no backslash can end up in front of a quote), no trailing slash, and
        /// every "%" doubled so a folder name can never be read as a yt-dlp template field.
        /// </summary>
        internal static string NormalizeFolder(string? folder)
        {
            string value = (folder ?? "").Trim();
            if (value.Length == 0) return DefaultFolder;
            if (value.Any(char.IsControl) || value.Contains('"')) throw new ArgumentException("The download folder contains characters that cannot be used.");

            value = value.Replace('\\', '/');
            while (value.Length > 1 && value.EndsWith('/') && !(value.Length == 3 && value[1] == ':')) value = value[..^1];
            return value.Replace("%", "%%");
        }

        private static string Join(string folder, string relative) => folder.EndsWith('/') ? folder + relative : folder + "/" + relative;

        // ------------------------------------------------------------------ rendering and the final safety check

        private static MappedDownload Finish(List<string> args, DownloadAuth? auth)
        {
            string command = Render(args);
            if (!CommandValidator.TryValidate(command, out string reason))
                throw new FormatException($"The command was refused by the command checker: {reason}.");
            return new MappedDownload(args, command, auth?.Cookies, auth?.UserAgent);
        }

        /// <summary>Quoted like the extension quotes it, so the string is readable and round-trips through <see cref="CommandTokenizer"/>.</summary>
        internal static string Render(IReadOnlyList<string> args)
        {
            var text = new StringBuilder("yt-dlp");
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (arg.Contains('"')) throw new FormatException("An argument contains a double quote, which a command string cannot hold.");

                bool quote = arg.Length == 0
                    || arg.Any(char.IsWhiteSpace)
                    || (i > 0 && Array.IndexOf(AlwaysQuotedAfter, args[i - 1]) >= 0)
                    || arg.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                text.Append(' ').Append(quote ? "\"" + arg + "\"" : arg);
            }
            return text.ToString();
        }
    }
}
