# Browser <-> host message protocol (version 1)

Status: contract and framing implemented in `host/YTDLPHost.Core/Native/` and unit-tested (Phase 2, step 2.1). The shim, the
app-side server and the extension client are later steps; nothing in the running app uses this yet.

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
| `urls` | required array, 1-500 entries, each an absolute `http`/`https` URL of 1-2048 characters without spaces or control characters (so a value can never look like an option) |
| `options.quality` | required: `"audio"`, `"custom"`, or a height as digits from 144 to 4320 (`"0720"` is normalised to `"720"`) |
| `options.audioFormat` | `mp3` (default), `m4a`, `aac`, `opus`, `vorbis`, `flac`, `wav` |
| `options.subs`, `compat`, `sponsorBlock`, `embedMetadata`, `playlist` | booleans, default `false` |
| `options.embedThumbnail` | boolean, default `true` |
| `options.playlistItems` | like `1-5,8`; needs `playlist: true` |
| `options.trim.start` / `end` | times such as `90`, `1:30`, `01:15:30.5`; both empty = no trim |
| `options.concurrentFragments` | whole number 1-16, default 4 |
| `options.custom` | raw yt-dlp flags, **required with** `quality: "custom"` and **only** then, max 4096 characters, no control characters. The host still checks them with its flag allowlist before use |
| `options.job` | `video` (default) or `channel`; a channel job needs `archiveName` (1-80 characters from `A-Z a-z 0-9 . _ -`), other jobs must not have one |
| `auth.cookies` | Netscape cookie text, at most 2 MB, no NUL characters |
| `auth.userAgent` | printable ASCII, at most 512 characters |

The extension never sends a **save folder**: the folder is a setting of the host app (to be added in step 2.2).

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

## Notes for later steps

- Step 2.2 maps a validated `DownloadRequest` to a yt-dlp argument list (reusing `YtDlpCommandBuilder`), with golden tests against
  what the extension builds today, and adds the host-side download-folder setting.
- The Firefox manifest uses `allowed_extensions` with the gecko id instead of `allowed_origins`; not verified yet.
