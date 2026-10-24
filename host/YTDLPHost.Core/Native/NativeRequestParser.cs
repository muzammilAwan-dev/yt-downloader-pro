using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YTDLPHost.Native
{
    /// <summary>
    /// Turns the bytes of one browser message into a typed request, or says exactly why it is refused. Deliberately strict
    /// (ADR-003): the protocol version must match, unknown fields and duplicate fields are errors, every value is checked
    /// for type, length and allowed characters, and the host builds the yt-dlp arguments itself from the typed result.
    /// Nothing here touches files, processes or the network.
    /// </summary>
    public static class NativeRequestParser
    {
        private static readonly Regex IdPattern = new(@"^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
        private static readonly Regex PlaylistItemsPattern = new(@"^\d+(-\d+)?(,\d+(-\d+)?)*$", RegexOptions.Compiled);
        private static readonly Regex TimePattern = new(@"^\d+(:\d{1,2}){0,2}(\.\d+)?$", RegexOptions.Compiled);
        private static readonly Regex ArchiveNamePattern = new(@"^[A-Za-z0-9._-]{1,80}$", RegexOptions.Compiled);
        private static readonly HashSet<string> AudioFormats = new(StringComparer.Ordinal) { "mp3", "m4a", "aac", "opus", "vorbis", "flac", "wav" };

        public static NativeParseResult Parse(byte[] utf8Json)
        {
            string? id = null;
            try
            {
                using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { MaxDepth = 8 });
                var root = new Obj(document.RootElement, "");

                id = ReadIdOrNull(root);
                int version = root.RequiredInt("v");
                if (version != NativeProtocol.Version)
                    throw new FieldException(NativeErrorCode.BadVersion, $"protocol version {version} is not supported (this host speaks {NativeProtocol.Version})", "v");
                if (id == null)
                    throw new FieldException(NativeErrorCode.BadField, "'id' is required: 1-64 characters from A-Z a-z 0-9 _ -", "id");

                string type = root.RequiredString("type", 32);
                switch (type)
                {
                    case "hello":
                        root.RejectUnknown("v", "id", "type");
                        return Ok(new HelloRequest(id), id);
                    case "download":
                        return Ok(ParseDownload(id, root), id);
                    default:
                        throw new FieldException(NativeErrorCode.BadType, $"unknown message type '{Clip(type)}'", "type");
                }
            }
            catch (FieldException ex)
            {
                return new NativeParseResult(null, new NativeParseError(ex.Code, ex.Message, ex.Field), id);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            {
                // Bad syntax, too deep, or text that is not valid UTF-8.
                return new NativeParseResult(null, new NativeParseError(NativeErrorCode.BadJson, "the message is not valid JSON (or is nested too deeply)"), null);
            }
        }

        private static NativeParseResult Ok(NativeRequest request, string id) => new(request, null, id);

        private static string? ReadIdOrNull(Obj root)
        {
            if (root.Optional("id") is not JsonElement element || element.ValueKind != JsonValueKind.String) return null;
            string value = element.GetString()!;
            return IdPattern.IsMatch(value) ? value : null;
        }

        // ------------------------------------------------------------------ download

        private static DownloadRequest ParseDownload(string id, Obj root)
        {
            root.RejectUnknown("v", "id", "type", "urls", "options", "auth");

            var urls = ParseUrls(root.Required("urls"));
            var options = ParseOptions(new Obj(root.Required("options"), "options."));
            DownloadAuth? auth = root.Optional("auth") is JsonElement authElement ? ParseAuth(new Obj(authElement, "auth.")) : null;

            return new DownloadRequest(id, urls, options, auth);
        }

        private static List<string> ParseUrls(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Array) throw Wrong("urls", "an array");
            int count = element.GetArrayLength();
            if (count < 1 || count > NativeProtocol.MaxUrls)
                throw Bad("urls", $"must contain between 1 and {NativeProtocol.MaxUrls} URLs");

            var urls = new List<string>(count);
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                urls.Add(ValidateUrl(item, $"urls[{index}]"));
                index++;
            }
            return urls;
        }

        // Only absolute http(s) URLs: that also means a value can never start with "-" and be mistaken for an option.
        private static string ValidateUrl(JsonElement item, string field)
        {
            if (item.ValueKind != JsonValueKind.String) throw Wrong(field, "a string");
            string url = item.GetString()!;
            if (url.Length == 0 || url.Length > NativeProtocol.MaxUrlLength) throw Bad(field, $"must be 1-{NativeProtocol.MaxUrlLength} characters long");
            foreach (char c in url)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c)) throw Bad(field, "must not contain spaces or control characters");
            }
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host))
                throw Bad(field, "must be an absolute http or https URL");
            return url;
        }

        private static DownloadOptions ParseOptions(Obj o)
        {
            o.RejectUnknown("quality", "audioFormat", "subs", "compat", "sponsorBlock", "embedMetadata", "embedThumbnail",
                            "playlist", "playlistItems", "trim", "concurrentFragments", "custom", "job", "archiveName");

            string quality = o.RequiredString("quality", 8);
            if (quality != "audio" && quality != "custom")
            {
                if (!int.TryParse(quality, NumberStyles.None, CultureInfo.InvariantCulture, out int height) || height < 144 || height > 4320)
                    throw Bad("options.quality", "must be 'audio', 'custom' or a video height from 144 to 4320");
                quality = height.ToString(CultureInfo.InvariantCulture); // "0720" -> "720"
            }

            string audioFormat = o.OptionalString("audioFormat", 8) ?? "mp3";
            if (!AudioFormats.Contains(audioFormat)) throw Bad("options.audioFormat", "is not a supported audio format");

            bool playlist = o.OptionalBool("playlist", false);
            string? playlistItems = EmptyToNull(o.OptionalString("playlistItems", 200));
            if (playlistItems != null)
            {
                if (!PlaylistItemsPattern.IsMatch(playlistItems)) throw Bad("options.playlistItems", "must look like '1-5,8'");
                if (!playlist) throw Bad("options.playlistItems", "needs options.playlist to be true");
            }

            TrimRange? trim = o.Optional("trim") is JsonElement trimElement ? ParseTrim(new Obj(trimElement, "options.trim.")) : null;

            int fragments = o.OptionalInt("concurrentFragments") ?? 4;
            if (fragments < 1 || fragments > 16) throw Bad("options.concurrentFragments", "must be between 1 and 16");

            string? custom = EmptyToNull(o.OptionalString("custom", NativeProtocol.MaxCustomChars));
            if (custom != null && custom.Any(char.IsControl)) throw Bad("options.custom", "must not contain control characters");
            if (quality == "custom" && custom == null) throw Bad("options.custom", "is required when quality is 'custom'");
            if (quality != "custom" && custom != null) throw Bad("options.custom", "is only allowed when quality is 'custom'");

            string job = o.OptionalString("job", 16) ?? "video";
            if (job != "video" && job != "channel") throw Bad("options.job", "must be 'video' or 'channel'");
            string? archiveName = EmptyToNull(o.OptionalString("archiveName", 80));
            if (job == "channel")
            {
                if (archiveName == null || !ArchiveNamePattern.IsMatch(archiveName))
                    throw Bad("options.archiveName", "is required for a channel job: 1-80 characters from A-Z a-z 0-9 . _ -");
            }
            else if (archiveName != null)
            {
                throw Bad("options.archiveName", "is only allowed for a channel job");
            }

            return new DownloadOptions(
                quality, audioFormat,
                o.OptionalBool("subs", false), o.OptionalBool("compat", false), o.OptionalBool("sponsorBlock", false),
                o.OptionalBool("embedMetadata", false), o.OptionalBool("embedThumbnail", true),
                playlist, playlistItems, trim, fragments, custom, job, archiveName);
        }

        private static TrimRange? ParseTrim(Obj t)
        {
            t.RejectUnknown("start", "end");
            string? start = ReadTime(t, "start");
            string? end = ReadTime(t, "end");
            return start == null && end == null ? null : new TrimRange(start, end);
        }

        private static string? ReadTime(Obj t, string name)
        {
            string? value = EmptyToNull(t.OptionalString(name, 16));
            if (value != null && !TimePattern.IsMatch(value)) throw Bad($"options.trim.{name}", "must be a time such as 90, 1:30 or 01:15:30.5");
            return value;
        }

        private static DownloadAuth? ParseAuth(Obj a)
        {
            a.RejectUnknown("cookies", "userAgent");

            string? cookies = EmptyToNull(a.OptionalString("cookies", NativeProtocol.MaxCookieChars));
            if (cookies != null && cookies.Contains('\0')) throw Bad("auth.cookies", "must not contain NUL characters");

            string? userAgent = EmptyToNull(a.OptionalString("userAgent", NativeProtocol.MaxUserAgentChars));
            if (userAgent != null && userAgent.Any(c => c < 0x20 || c > 0x7E)) throw Bad("auth.userAgent", "must be printable ASCII");

            return cookies == null && userAgent == null ? null : new DownloadAuth(cookies, userAgent);
        }

        // ------------------------------------------------------------------ helpers

        private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static string Clip(string text)
        {
            var clean = new string(text.Where(c => !char.IsControl(c)).ToArray());
            return clean.Length <= 60 ? clean : clean[..60] + "...";
        }

        private static FieldException Bad(string field, string what) => new(NativeErrorCode.BadField, $"'{Clip(field)}' {what}", field);
        private static FieldException Wrong(string field, string expected) => Bad(field, $"must be {expected}");

        private sealed class FieldException : Exception
        {
            public NativeErrorCode Code { get; }
            public string? Field { get; }

            public FieldException(NativeErrorCode code, string message, string? field) : base(message)
            {
                Code = code;
                Field = field;
            }
        }

        /// <summary>One JSON object with duplicate detection and typed, checked getters. A JSON null counts as "absent".</summary>
        private sealed class Obj
        {
            private readonly Dictionary<string, JsonElement> _props = new(StringComparer.Ordinal);
            private readonly string _prefix;

            public Obj(JsonElement element, string prefix)
            {
                _prefix = prefix;
                if (element.ValueKind != JsonValueKind.Object)
                {
                    string name = prefix.Length == 0 ? "the message" : prefix.TrimEnd('.');
                    throw new FieldException(NativeErrorCode.BadField, $"{Clip(name)} must be a JSON object", prefix.Length == 0 ? null : prefix.TrimEnd('.'));
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (!_props.TryAdd(property.Name, property.Value))
                        throw Bad(prefix + property.Name, "appears more than once");
                }
            }

            public void RejectUnknown(params string[] allowed)
            {
                foreach (var name in _props.Keys)
                {
                    if (Array.IndexOf(allowed, name) < 0)
                        throw new FieldException(NativeErrorCode.BadField, $"unknown field '{Clip(_prefix + name)}'", _prefix + name);
                }
            }

            public JsonElement? Optional(string name) =>
                _props.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

            public JsonElement Required(string name) =>
                Optional(name) ?? throw new FieldException(NativeErrorCode.BadField, $"missing required field '{Clip(_prefix + name)}'", _prefix + name);

            public string RequiredString(string name, int maxLength) => ReadString(Required(name), name, maxLength);

            public string? OptionalString(string name, int maxLength) =>
                Optional(name) is JsonElement element ? ReadString(element, name, maxLength) : null;

            public bool OptionalBool(string name, bool defaultValue)
            {
                if (Optional(name) is not JsonElement element) return defaultValue;
                return element.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => throw Wrong(_prefix + name, "true or false")
                };
            }

            public int RequiredInt(string name) => ReadInt(Required(name), name);

            public int? OptionalInt(string name) => Optional(name) is JsonElement element ? ReadInt(element, name) : null;

            private string ReadString(JsonElement element, string name, int maxLength)
            {
                if (element.ValueKind != JsonValueKind.String) throw Wrong(_prefix + name, "a string");
                string value = element.GetString()!;
                if (value.Length > maxLength) throw Bad(_prefix + name, $"is longer than {maxLength} characters");
                return value;
            }

            private int ReadInt(JsonElement element, string name)
            {
                if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int value))
                    throw Wrong(_prefix + name, "a whole number");
                return value;
            }
        }
    }
}
