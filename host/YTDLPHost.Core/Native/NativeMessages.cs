namespace YTDLPHost.Native
{
    /// <summary>Constants of the browser &lt;-&gt; host message contract (version 1). See docs/NATIVE_PROTOCOL.md.</summary>
    public static class NativeProtocol
    {
        public const int Version = 1;
        public const string HostName = "com.ytdlppro.host";

        public const int MaxUrls = 500;
        public const int MaxUrlLength = 2048;
        public const int MaxCookieChars = 2 * 1024 * 1024;
        public const int MaxCustomChars = 4096;
        public const int MaxUserAgentChars = 512;
    }

    public enum NativeErrorCode { BadJson, BadVersion, BadType, BadField }

    /// <summary>Why a message was refused. <see cref="Field"/> names the offending field when there is one.</summary>
    public sealed record NativeParseError(NativeErrorCode Code, string Message, string? Field = null)
    {
        /// <summary>The wire name used in a "rejected" response.</summary>
        public string CodeName => Code switch
        {
            NativeErrorCode.BadJson => "bad_json",
            NativeErrorCode.BadVersion => "bad_version",
            NativeErrorCode.BadType => "bad_type",
            _ => "bad_field"
        };
    }

    /// <summary>A message from the browser. Only valid, fully checked messages ever become one of these.</summary>
    public abstract record NativeRequest(string Id);

    /// <summary>Handshake: the extension asks which host and protocol it is talking to.</summary>
    public sealed record HelloRequest(string Id) : NativeRequest(Id);

    public sealed record TrimRange(string? Start, string? End);

    /// <summary>Login material for sites that need it. Either part may be absent.</summary>
    public sealed record DownloadAuth(string? Cookies, string? UserAgent);

    /// <param name="Quality">"audio", "custom", or a video height as plain digits ("1080").</param>
    /// <param name="Custom">Raw yt-dlp flags, only with Quality "custom"; they still go through the host's flag allowlist.</param>
    /// <param name="Job">"video" (one URL or playlist) or "channel" (profile / uploads job, needs an ArchiveName).</param>
    /// <param name="CropKeyframeCuts">Re-encode at the cut points (--force-keyframes-at-cuts). The extension sends false for sites that stream HLS (Twitch).</param>
    /// <param name="UniqueFilename">Put the video id into the file name (sites whose captions repeat, such as TikTok).</param>
    public sealed record DownloadOptions(
        string Quality,
        string AudioFormat,
        bool Subs,
        bool Compat,
        bool SponsorBlock,
        bool EmbedMetadata,
        bool EmbedThumbnail,
        bool Playlist,
        string? PlaylistItems,
        TrimRange? Trim,
        int ConcurrentFragments,
        string? Custom,
        string Job,
        string? ArchiveName,
        bool CropKeyframeCuts,
        bool UniqueFilename);

    public sealed record DownloadRequest(string Id, IReadOnlyList<string> Urls, DownloadOptions Options, DownloadAuth? Auth) : NativeRequest(Id);

    /// <param name="Id">The request id when it could be read, so even a refusal can be matched to its request.</param>
    public sealed record NativeParseResult(NativeRequest? Request, NativeParseError? Error, string? Id)
    {
        public bool Ok => Request != null;
    }
}
