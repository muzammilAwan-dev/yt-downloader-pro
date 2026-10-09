# Architecture decisions (cross-platform effort)

Status: proposed. Each entry is one line of context, the decision, and what it costs.

**ADR-001 - UI toolkit: Avalonia.**
WPF is Windows-only; the view models, services and queue logic are C#. Avalonia keeps XAML/MVVM and CommunityToolkit.Mvvm, so only views are ported. Cost: re-skin `App.xaml`/`MainWindow.xaml`; Linux tray needs a fallback on GNOME.

**ADR-002 - Browser-to-host transport: Native Messaging (stdio), not a `ytdlp://` protocol.**
The protocol handler is registry-based, exposes cookies on a process command line and in a payload file, and has no reply channel. Native Messaging pins access to our extension ID and allows live progress. Cost: a small shim executable, per-browser manifest registration, a pinned extension ID. `ytdlp://` stays as a fallback for one release.

**ADR-003 - Messages are structured JSON, not a command string.**
The host currently re-parses a shell-style string the extension built. The host will instead accept typed options and build the yt-dlp argument array itself. Cost: extension and host change together; version field in every message.

**ADR-004 - Self-contained builds.**
CI built framework-dependent while `Publish.bat` built self-contained. Releases are self-contained so users need no runtime. Cost: larger download (compression enabled).

**ADR-005 - One version number.**
`host/Directory.Build.props` is the source; `extension/manifest.json` and `host/installer/setup.iss` must match, enforced by `tools/check_versions.py` in CI.

**ADR-006 - Engine binaries are pinned and verified.**
yt-dlp, ffmpeg and deno are fetched per OS/CPU, checksum-verified and installed atomically; no blind "latest". (Implemented in Phase 3.)
