# UI/UX setup and native preview evidence

Date: 15 November 2026. Desktop app first; extension implementation deferred.

> Historical setup evidence. For current layout measurements use [UI-UX-AUDIT.md](UI-UX-AUDIT.md): its direct-content captures correct a later-discovered `VisualBrush` content-bounds spacing distortion in the images below. Static-palette observations remain useful, but these earlier images are not precise layout/scaling evidence.

## Setup outcome
- Six original, project-specific design skills created under `.opencode/skills/` and successfully loaded through the skill tool.
- Eighteen research references recorded in [UI-UX-SOURCES.md](UI-UX-SOURCES.md).
- `/ui-audit`, `/ui-plan` and `/ui-review` configured in `opencode.jsonc`. JSONC parsing, command definitions, skill frontmatter and instruction paths checked; commands were not invoked as end-to-end CLI tests. The `opencode` executable was not on PATH.
- [UI-UX-PLAYBOOK.md](UI-UX-PLAYBOOK.md) and `AGENTS.md` establish evidence, scope and approval boundaries.
- No application/extension source edits, Git initialization, publishing, SDK/runtime installation, external UI packages, fonts, plugins or community skill scripts.

## Build and environment
`dotnet build host/YTDLPHost.sln --configuration Debug --nologo`

Result: **successful, 0 warnings, 0 errors**, elapsed 30.52 seconds. Subsequent temporary-harness builds also succeeded with 0 warnings/errors.

Installed SDK: 10.0.401. Windows Desktop runtimes: 9.0.8 and 10.0.12; **8.x Windows Desktop is absent**. Having .NET Core 8.0.14 does not supply WPF's Desktop runtime. The .NET 8 target was not changed.

Preview launch used explicit `dotnet --roll-forward Major`, resulting in **.NET 9.0.8**. This does not establish .NET 8 runtime compatibility or installed-product parity.

## Native rendering method and limits
The temporary harness loads the **compiled `MainWindow`**, the actual `DownloadTask` and `DownloadItemViewModel`, and the source `App.xaml` resource dictionary. It does not recreate the layout in HTML.

Resource parsing adds assembly-qualified converter names. For static light/dark previews, brush colors and accent Color resources are initialized from the actual `ThemeService` palette before parsing; no invented palette is used. Separate resource dictionaries/windows are constructed for the two themes.

A synthetic root queue replaces `MainViewModel`. Commands are no-ops; filter counts and footer content are fixture-controlled. Normal `App` startup, protocol registration, payload consumption, queue/history restoration, tray services, engine updates, downloads and file actions are bypassed.

The client-area capture composites `Window.Background` behind the rendered content. Native title bars, Snap behavior, real screen text rasterization and unrelated desktop content are not captured.

Host display DPI was **144 (150%)**. Output uses a 96-DPI `RenderTargetBitmap`, mapping client DIPs to pixels; these images are **not physical 150%-scale screen captures**. Default window: 980 x 700 DIPs, client output 966 x 663 pixels. Minimum window: 840 x 540 DIPs, client output 826 x 503 pixels.

Harness location (temporary, not part of app source):
`C:\Users\Malku\AppData\Local\Temp\opencode\ytdlp-ui-baseline\`

### Capture-fidelity investigation
The initial captures omitted the window background and falsely labeled dark-looking output as light. They are excluded from this evidence set.

Instrumentation showed parsed resource brushes were frozen. `ThemeService.SetMode(Light)` replaced dictionary entries, while existing `StaticResource` consumers retained old brushes (`Window.Background` remained `#181818`). See [brush diagnostics](ui-baseline/brush-diagnostics.txt). Fresh resources initialized to the selected palette corrected static preview fidelity.

**Runtime theme switching remains unresolved for the real app.** This harness result does not establish a production defect or prove compiled-resource behavior. Verify normal startup and switching separately on the intended runtime before making that conclusion.

## Captures visually inspected
All five corrected images were opened and inspected. They contain synthetic titles and no private URLs/cookies.

| Capture | Presentation inspected |
| --- | --- |
| [Empty, dark](ui-baseline/01-empty-dark.png) | Existing empty-state copy and layout |
| [Queue, dark](ui-baseline/02-queue-dark.png) | Downloading, paused, completed and failed cards; long title; progress and recovery copy |
| [Queue, light](ui-baseline/03-queue-light.png) | Same fixture with static light resources |
| [Limits, light](ui-baseline/04-limits-light.png) | Expanded limits panel at default size |
| [Minimum size, limits, light](ui-baseline/05-minimum-limits-light.png) | Limits-panel wrapping and partial queue viewport at minimum size |

Raw capture metadata: [preview context](ui-baseline/preview-context.txt).

### Evidence distinctions
- **Source observation:** shared semantic brush resources, native controls, computed task presentations, filters and limits already exist.
- **Rendered observation:** the corrected synthetic previews display distinct title/status/action areas, state glyphs plus words, completion actions, error recovery guidance and limits-panel content. This records existing strengths, not approval of all usability/accessibility details.
- **Hypotheses:** action discoverability, density, contrast and task-flow friction need a focused audit and interactive checks. No redesign choice is approved by this setup.

## Not exercised
- Live downloads, browser handoff, startup, queue restoration, cancellation/retention or engine updates.
- Queued, processing/indeterminate, cancelled, missing-metadata and zero-filter-result presentations; expanded logs/details and settings validation.
- Keyboard-only navigation, focus restoration, UI Automation, Narrator or measured contrast.
- System theme following, runtime theme switching, Windows contrast themes, reduced motion, enlarged text, physical 100/150/200% scaling or Snap layouts.
- Large-queue responsiveness, scrolling performance or virtualization behavior.

A successful build and synthetic screenshots are **not** complete UX or accessibility validation.

## Source preservation
54 files under `host/` and `extension/`, excluding `bin/obj`, matched the pre-setup aggregate SHA-256:
`101BEA819EEB5B999ACEA5917FBC8B86E13F7067DD8450130A5138EBBDE8CF88`.

Next phase: audit these current-source native presentations and critical interactions, then propose two focused app directions and one small implementation slice. Confirm major layout/identity changes before editing application code.
