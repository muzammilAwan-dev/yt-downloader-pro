# Browser <-> host message protocol (version 1)

Status: contract and framing implemented in `host/YTDLPHost.Core/Native/` and unit-tested (Phase 2, step 2.1); request -> yt-dlp
arguments in step 2.2; the app-side pipe server in step 2.3 (see "App side" below). The shim and the extension client are later steps.

## Transport (verified against the Chrome Native Messaging docs)

- The browser starts the host (our small shim) and talks to it over **stdin / stdout**.
- Each message is **UTF-8 JSON**, preceded by a **32-bit length in the machine's native byte order**.
- Host -> browser: at most **1 MB** per message (Chrome's limit). Browser -> host: Chrome allows 4 GB; we accept at most
  **8 MB** (`NativeFrame.MaxBrowserToHostBytes`) to bound memory.
- `connectNative` keeps one host process alive for the port's lifetime (used for live progress); `sendNativeMessage` starts a new
  process per message and only the first reply counts. The extension uses `connectNative`.
- Chrome passes the caller's origin (`chrome-extension://<id>/`) as the host's first argument; the shim checks it against the pinned
  extension id. `allowed_origins` in the host manifest also pins it (no wildcards allowed).
- **Never write anything but protocol frames to stdout**; log to a file or stderr. On Windows use the raw stdout stream, not
  `Console.Out` (text mode would corrupt the length bytes).
- Host name: `com.ytdlppro.host` (lowercase letters, digits, underscores, dots only).

## Envelope

Every message is a JSON object with `"v": 1` and an `"id"` (1-64 characters from `A-Z a-z 0-9 _ -`, chosen by the sender; replies
echo it). Unknown fields, duplicate fields, a wrong `v`, wrong types and out-of-range values are all **refused**, never ignored.
JSON `null` counts as "absent". Maximum JSON nesting depth: 8.

## Browser -> host

### `hello`
`{ "v":1, "id":"h1", "type":"hello" }` - no other fields. Reply: `hello`.

### `download`
```json
{ "v":1, "id":"d1", "type":"download",
  "urls": ["https://www.youtube.com/watch?v=abcdefghijk"],
  "options": {
    "quality": "1080",
    "audioFormat": "mp3",
    "subs": false, "compat": false, "sponsorBlock": false,
    "embedMetadata": false, "embedThumbnail": true,
    "playlist": false, "playlistItems": "1-5,8",
    "trim": { "start": "01:15", "end": "01:25.5" },
    "concurrentFragments": 4,
    "custom": null,
    "job": "video", "archiveName": null
  },
  "auth": { "cookies": "<Netscape cookie file text>", "userAgent": "Mozilla/5.0 ..." } }
```

| Field | Rule |
|---|---|
| `urls` | required array, 1-500 entries, each an absolute `http`/`https` URL of 1-2048 characters without spaces, control characters or double quotes (so a value can never look like an option). A video job makes one download per URL; a channel job is ONE download: the first URL is the page, the rest are direct item links |
| `options.quality` | required: `"audio"`, `"custom"`, or a height as digits from 144 to 4320 (`"0720"` is normalised to `"720"`) |
| `options.audioFormat` | `mp3` (default), `m4a`, `aac`, `opus`, `vorbis`, `flac`, `wav` |
| `options.subs`, `compat`, `sponsorBlock`, `embedMetadata`, `playlist` | booleans, default `false` |
| `options.embedThumbnail` | boolean, default `true` |
| `options.playlistItems` | like `1-5,8`; needs `playlist: true` |
| `options.trim.start` / `end` | times such as `90`, `1:30`, `01:15:30.5`; both empty = no trim |
| `options.concurrentFragments` | whole number 1-16, default 4 |
| `options.custom` | raw yt-dlp flags, **required with** `quality: "custom"` and **only** then, max 4096 characters, no control characters. The host still checks them with its flag allowlist before use |
| `options.cropKeyframeCuts` | boolean, default `true`: re-encode at the cut points (`--force-keyframes-at-cuts`). The extension sends `false` for sites that stream HLS (Twitch) |
| `options.uniqueFilename` | boolean, default `false`: put the video id in the file name (TikTok-style repeated captions) |
| `options.job` | `video` (default) or `channel`; a channel job needs `archiveName` (1-80 characters from `A-Z a-z 0-9 . _ -`), other jobs must not have one |
| `auth.cookies` | Netscape cookie text, at most 2 MB, no NUL characters |
| `auth.userAgent` | printable ASCII, at most 512 characters |

A channel job refuses `subs`, `sponsorBlock`, `playlist`, `trim` and `quality: "custom"`.

The extension never sends a **save folder**: it is the host setting `DownloadFolder` in `settings.json` (empty = `~/Downloads/YTDownloaderPro`). Until Phase 3 replaces the path validator, a custom folder must start with a drive letter (`D:\Videos`) or `~`; Unix-style absolute paths are refused.

## Effective choices (the extension's duty)

The options in a request are the choices AFTER per-site gating, because the host knows nothing about individual sites. For example
`subs` = the user's tick AND the site supports subtitles AND not audio; `playlist` = tick AND site supports playlists; `embedMetadata` =
setting AND site supports it; `embedThumbnail` = setting (not false) AND site supports it; `sponsorBlock` and `compat` likewise;
`cropKeyframeCuts` = site flag; `uniqueFilename` = site flag; `trim` only when a start or end time was entered.
`tools/generate-native-golden.js` holds these derivations next to the extension's current command-building code and is the reference
for the future `native.js`.

## From request to yt-dlp arguments (step 2.2)

`DownloadRequestMapper.Map` builds the yt-dlp arguments, in the same order and with the same flags the extension builds today (29 golden
cases generated from the extension's own code prove it). The result also carries a quoted `Command` string that round-trips through
`CommandTokenizer`; every command is checked by `CommandValidator` before it is returned, so a mapping bug fails closed. `%` in the
folder is doubled so it can never start a yt-dlp template field.

## Host -> browser

| Message | Fields |
|---|---|
| `hello` | `hostVersion`, `protocol` |
| `accepted` | the request was valid and queued |
| `rejected` | `code` (`bad_json`, `bad_version`, `bad_type`, `bad_field`), `reason` (cut to 300 characters), optional `field`; `id` is omitted when the request was too broken to read one |
| `progress` | `percent` (one decimal), `speed`, `eta`, `phase`, `title` |
| `done` | optional `note` (for example "file already existed, nothing new downloaded") |
| `error` | `reason` (cut to 300 characters) |

All carry `"v":1` and the request `id`, and are far below Chrome's 1 MB limit.

## App side (step 2.3)

The running app listens on a **named pipe** `YTDLPHost_Native_<user>` (`NativePipe.NameForCurrentUser()`; letters, digits, `_ - .` of the
Windows user name, at most 32). The pipe is created with `PipeOptions.CurrentUserOnly`, so only the same user (account AND elevation level on
Windows) can connect; the shim must open it with the same option (`NativePipe.ClientOptions`). It carries the **same length-prefixed JSON
frames** as the browser's stdio, so the shim only copies frames in both directions after checking the caller's origin.

- Up to 8 shims at once. A broken frame (truncated, or larger than 8 MB) closes that connection.
- `hello` is answered at once. A `download` is parsed, mapped (`DownloadRequestMapper`, folder from `settings.json`), and queued through the
  same intake and queue as a `ytdlp://` link (the command is validated again; cookies go to a temp file, never into the command; the
  browser's User-Agent is added only for non-YouTube sites and only if it passes the character check). Then `accepted`.
- A request the mapper refuses gets `rejected` (`bad_field`). If the app cannot take downloads at all (first-time setup failed) the
  browser gets `error` instead of `accepted`.
- **One request, one final message.** A video job with several URLs makes several downloads; `progress` is their average (`phase` reads
  "2/5 finished"), and the end is ONE `done` (every download finished; `note` only for a single download), ONE `done` with a note
  such as "3 finished, 1 failed: reason" (some failed), or ONE `error` (all failed: "2 of 2 downloads failed: reason").
- Sending the same download again while it is queued or running does not add a second item: the browser follows the existing one.
- `progress` is sent when something changed, polled every 500 ms. A paused download keeps reporting `phase: "Paused"`. A download that was
  cancelled or removed in the app ends with `error`.
- Closing the connection stops the reporting only; **downloads belong to the app** and carry on.
- The app does not bring its window to the front for a browser download (the old `ytdlp://` path did).

## Shim (step 2.4)

`host/YTDLPHost.Shim` builds `ytdlp-shim.exe`, the program the browser starts (one process per `connectNative` port). It never reads the JSON.

1. **Who started me.** The first argument must be exactly one of the origins listed in `allowed-origins.txt` next to the exe (one per line,
   `#` comments allowed, trailing slash included, for example `chrome-extension://<id>/`). Anything else: exit code 2, nothing is
   forwarded. This is the second lock behind the manifest's `allowed_origins`. Chrome on Windows may add `--parent-window=...` after the
   origin; it is ignored. Firefox passes different arguments (manifest path and extension id) and is not supported yet.
   The installer or the app writes `allowed-origins.txt` together with the host manifest (step 2.6). Without the file nothing starts.
2. **Find or start the app.** Connects to the pipe `NativePipe.NameForCurrentUser()` with `NativePipe.ClientOptions` (same user only). If
   nobody listens, it starts `YTDLPHost.exe` from its own folder and retries for up to 20 s. A pipe that exists but belongs to another user or
   elevation level is not retried (exit code 4; the app probably runs as administrator).
3. **Copy frames both ways** until either side ends, enforcing the size limits (8 MB from the browser, 1 MB to the browser). A broken frame ends the
   bridge; the browser sees the port disconnect, and the extension treats that as "host unavailable".
4. **stdout is only for frames.** `Console.Out` is replaced by a null writer; everything else goes to `%LOCALAPPDATA%\YTDownloaderProEngine\shim.log`
   (trimmed at 200 KB).

Exit codes: 0 normal end, 2 caller not allowed, 3 app unreachable or could not be started, 4 app refused the connection.

## Notes for later steps

- Step 2.2 maps a validated `DownloadRequest` to a yt-dlp argument list (reusing `YtDlpCommandBuilder`), with golden tests against
  what the extension builds today, and adds the host-side download-folder setting.
- The Firefox manifest uses `allowed_extensions` with the gecko id instead of `allowed_origins`; not verified yet.
