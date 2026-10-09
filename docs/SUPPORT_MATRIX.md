# Support matrix (proposed - confirm before Phase 1)

| Tier | Meaning | Targets |
|---|---|---|
| 1 | Built, tested in CI and manually before every release | Windows 10/11 x64 · macOS 13+ Apple Silicon · Ubuntu 22.04/24.04 x64 (GNOME, X11 and Wayland) |
| 2 | Built in CI, tested on a best-effort basis | Windows 11 ARM64 · macOS 13+ Intel · Linux ARM64 · Fedora (current) · KDE Plasma |
| Out of scope | Not planned | Safari · mobile browsers · Flatpak/Snap-packaged browsers (until native messaging is verified there) · 32-bit |

## Browsers

| Tier | Browsers |
|---|---|
| 1 | Chrome, Edge, Brave (Chromium MV3) |
| 2 | Firefox (separate manifest, Phase 6), Opera, Vivaldi, Chromium |

## Rules that follow from this
- Every PR runs the unit tests on Windows and Linux; macOS joins after Phase 3.
- A change may not break a Tier 1 target. Tier 2 breakage is logged, not release-blocking.
- No telemetry. Diagnostics are an opt-in "export diagnostics" bundle only.
