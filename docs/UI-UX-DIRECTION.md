# Desktop design direction — proposal, not implementation

15 November 2026. Based on the [current-source audit](UI-UX-AUDIT.md). No application/extension code changed; major layout/identity choices await owner approval.

> Update: the owner approved recommended Direction A. Its focused implementation and verification are recorded in [UI-UX-IMPLEMENTATION.md](UI-UX-IMPLEMENTATION.md). The proposal below remains the original decision record, not a claim that every future acceptance gate passed.

## Recommendation
**A — Refined queue cards.** Keep the recognizable Windows utility and existing card-based queue. Strengthen task/action hierarchy and trustworthy state communication rather than applying a new dashboard skin.

The current design already has useful foundations: restrained neutrals, blue transfer accent, semantic status colors, native controls, readable titles, an inline limits panel and secondary diagnostics. Its largest observed problems are keyboard/automation identity and status honesty, not a lack of decorative content.

## Two focused concepts

These are layout sketches, **not rendered after-images**. Labels are actual supported actions; examples use the audit fixture. Neither option adds navigation pages, URL input, bulk tools or automatic sorting.

### A — Refined queue cards (recommended)
Preserve the header, contextual filters, inline limits and footer. Each card has a full-width title/quality line, a deliberate status/action line and honest progress treatment. Keep one **labeled main action per task state**, with cancel/remove and overflow secondary.

```text
YT Downloader Pro                              Limits   Hide   Settings
All 4   Active 2   Completed 1   Failed 1

┌ ↓ A walk through the mountains at sunrise                    1080p … ┐
│ Downloading video · 42.6%      3.30 MiB/s · ETA 00:22   [Pause] [×] │
│ ━━━━━━━━━━━━━━━━━━━─────────────────────────────────────────────── │
└───────────────────────────────────────────────────────────────────┘
┌ Ⅱ Acoustic session — live at the studio                        MP3 … ┐
│ Paused at 61.3%                                      [Resume] [×] │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━─────────────────────────────────────── │
└───────────────────────────────────────────────────────────────────┘
┌ ✓ Independent short film — behind the scenes                   720p … ┐
│ Completed · 10:42 AM                           [Show in folder] [▶] │
└───────────────────────────────────────────────────────────────────┘
┌ ! A very long documentary title…                             1080p … ┐
│ The site refused the download (HTTP 403)                   [Retry] │
│ Update the download engine from Settings and tools, then retry.    │
│ Show details   Copy error                                         │
└───────────────────────────────────────────────────────────────────┘
```

`×` remains the appropriate existing cancel/remove command for that state, with distinct names/retention explanation; the sketch does not change its semantics. Do not introduce a new delete-file operation. At minimum width, status wraps within its column without squeezing titles or pushing main actions offscreen.

**Tradeoffs:** familiar, low migration risk, supports long titles/errors/logs naturally. Cards still consume more vertical space than compact rows; do not promise increased queue capacity before rendering and measurement.

### B — Compact transfer rows
Replace separate card shells with a flatter aligned list. Title/state occupy the left; quality, live metrics and the primary action form consistent columns. Only failure recovery and logs expand vertically.

```text
YT Downloader Pro                              Limits   Hide   Settings
All 4   Active 2   Completed 1   Failed 1

↓ A walk through the mountains…          1080p    3.30 MiB/s   [Pause]
  Downloading video · 42.6% · ETA 00:22
  ━━━━━━━━━━━━━━━━━━━───────────────────────────────────────────────
Ⅱ Acoustic session — live at the studio    MP3                 [Resume]
  Paused at 61.3%
✓ Independent short film…                  720p        [Show in folder]
  Completed · 10:42 AM
! A very long documentary title…          1080p                 [Retry]
  The site refused the download (HTTP 403)
  Update the download engine from Settings and tools, then retry.
  Show details   Copy error
```

**Tradeoffs:** more compact and potentially faster to scan large queues, but substantially changes visual identity and layout. Fixed column rhythm must accommodate long titles, expanded logs, enlarged text and minimum width. Needs explicit owner approval and broader native validation. Density is a hypothesis, not a measured usability gain.

## Compact token/component plan for A

| Role | Proposed direction |
| --- | --- |
| Palette | Preserve existing `BackgroundBrush`, `SurfaceBrush`, text, accent, on-accent, status and focus resources in both palettes. No new global accent or isolated hex colors. |
| Typography | Existing Segoe UI; title 14 DIP semibold, body/status about 13 DIP, secondary metadata 12 DIP. Keep essential wording readable; not a font/package installation. Values are candidate design choices, not accessibility requirements. |
| Spacing | Document a 4/8/12/16/24 DIP rhythm. Keep card internals and action gaps consistent; avoid filling unused space with decorative panels. Render before committing exact gutters. |
| Geometry | Retain existing 8 DIP card and 4 DIP button corners. Comfortable native main controls around 32 DIP high; increase short text-link hit areas without making a false web AA target-size claim. |
| Main action | One stable labeled slot: Pause, Resume, Retry, or Show in folder. Neutral for routine actions; accent for resume/retry. Secondary cancel/remove/play/overflow remain available with correct parameters and names. |
| Progress | Preserve determinate transfer and paused fills. Processing: real phase words + indeterminate/stationary treatment, no speed/ETA or made-up completion percentage. Unknown-length transfer retains size-so-far/speed/elapsed where actually available. |
| Counts | Use an opaque semantic text brush; do not dim ordinary text by alpha. Both default palettes must clear 4.5:1 with margin. |
| Focus | Preserve the existing focus resource. Define queue traversal explicitly; visible focus scrolls into view, survives relevant state transitions, and returns sensibly after menus/disclosures. No hover-only action policy. |
| Native semantics | Safe item title names; named menu actions/edit fields/progress; actual theme selection state. Retain native Button/ComboBox/RadioButton/ListBox roles and patterns. |
| Motion | Existing brief card entry only when Windows allows animations; provide a stationary processing alternative. Do not animate each progress update. Reduced-motion changes need actual Windows verification. |

## State contract
- **Queued:** waiting text; no fabricated progress, speed, ETA or queue position. Unknown metadata should not imply active fetching.
- **Transfer:** truthful percentage/size/speed/ETA if known, with Pause/Cancel available.
- **Processing:** real conversion/merge/embedding phase, no transfer speed or elapsed label derived from stale ETA. Completion actions remain unavailable until actual completion.
- **Paused:** paused words/glyph, retained percentage if present, Resume and Cancel.
- **Completed:** actual completion/result note, Show in folder/Play; removal preserves media files.
- **Failed:** problem + verified next step, Retry, details/copy; removing the entry remains distinct from retry/cancel.
- **Cancelled:** explicit cancelled state; removal only, no imaginary undo/resume.
- **Diagnostics:** selectable text and per-item disclosure state; UI updates must not steal focus or reset reading position.

## UI writing candidates

| Current | Candidate | Behavior basis |
| --- | --- | --- |
| “Fetching video...” on a queued unknown-title task | “Untitled download” + existing “In queue” status | Title unavailable; extraction not guaranteed to have started. Use fetching wording only during actual extraction. |
| “from the gear menu” | “from Settings and tools” | Existing menu/button accessible name; avoid icon-dependent directions. No automatic engine update action added. |
| “All downloads finished” with failed entries | A truthful failure count alongside any remaining work | Existing statuses/counting can distinguish errors. Preserve unrelated engine status messages; exact grammar/localization must be tested. |

Existing browser empty-state guidance, HTTP-specific recovery and retention explanations generally remain. Do not rewrite every string for stylistic novelty.

## Proposed implementation boundary

### First: safe, accessible, honest queue + focused card refinement
1. UI-only tooltip safeguard (A1), with synthetic payload regression checks.
2. Queue traversal and names (A2/A3); inspect native peers and keyboard reachability before moving controls.
3. Processing presentation and count contrast (A4/A5/A6).
4. Refine only card hierarchy/action alignment according to A; preserve command parameters, converter logic, semantic names and state visibility.

**Likely files:**
- `host/YTDLPHost/MainWindow.xaml`: card layout, navigation policy, item/progress names and state visibility.
- `host/YTDLPHost/App.xaml`: semantic styles, count treatment, editable-control labeling/focus, optional stationary progress treatment.
- `host/YTDLPHost/MainWindow.xaml.cs`: explicit menu names/state and focused disclosure handling, if needed.
- `host/YTDLPHost/ViewModels/DownloadItemViewModel.cs`: safe presentation properties only, processing/placeholder wording and notifications.

No engine/protocol/persistence/cookie changes. Extend `ThemeService.cs` only if a genuine new semantic resource is needed, not to disguise the unresolved switching question.

**Deferred:** virtualization/recycling, live settings semantics and inline-validation recovery, tray redesign, broad footer changes, framework migration, new dependencies, branding replacement and all extension work. These are separately testable slices, not side effects of polish.

## Acceptance gates
- Build with 0 errors and no new warnings; run targeted presentation/safety checks.
- Match before/after seed, size, theme and render method against audit captures. Inspect all queue states, long titles, missing metadata, errors and expanded logs.
- Forward/reverse keyboard traversal reaches **every card**, including below the viewport. Verify activation, menus, dismissal, scroll/focus behavior and state changes—not merely `MoveFocus` output.
- Native names use task titles and actual actions, never view-model type names or payloads. Check UIA externally and Narrator; mark missing coverage explicitly.
- Default/selected/hover/focus text and meaningful UI visuals measured in light/dark. Count text meets 4.5:1; no palette-wide compliance claim.
- Inspect intended-runtime theme changes, contrast themes, reduced motion, minimum/default size, real scaling/text enlargement and Snap when available. .NET 9 synthetic renders alone cannot pass these gates.
- Preserve pause/cancel/remove/file retention, settings validation formats, handoff, history and cookie contracts. No live behavior test is inferred from a preview.

**Decision needed:** approve A's focused queue-card slice, choose the broader B layout, or keep the layout unchanged and address safety/accessibility only.
