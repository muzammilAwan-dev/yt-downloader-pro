# Desktop UI audit

15 November 2026. Scope: current WPF host; no app or extension edits. See [design proposal](UI-UX-DIRECTION.md).

> Historical pre-change audit. Recommended Direction A was subsequently approved and implemented; see [implementation and precise verification limits](UI-UX-IMPLEMENTATION.md). Findings below remain the baseline evidence, not a claim that deferred items were fixed.

## Evidence and environment
Current-source native previews, not outdated repository images. The latest harness build rebuilt the host successfully: **0 warnings, 0 errors**. Preview runtime: .NET 9.0.8 via explicit major roll-forward; .NET 8 Windows Desktop remains absent.

Seven synthetic client-area captures were opened and inspected. Source controls/view models are real; startup and the root `MainViewModel` are bypassed. Filter/footer content is fixture-controlled, file/engine commands are no-ops. Host DPI: 144; rendered output: 96 DPI, not physical display-scale screenshots. Native title bars are outside the captures.

An additional capture correction preserves transparent margins: paint the window background, then render the content **directly**. Earlier baseline captures used a default `VisualBrush`, which mapped content bounds and altered spacing. Use this audit's captures for layout evidence; retain earlier files only as historical setup evidence.

Evidence labels:
- **S:** source observation.
- **R:** rendered synthetic WPF / native-control observation.
- **H:** usability hypothesis, not observed user behavior.

Severity: S0 preference; S1 minor friction; S2 significant friction; S3 core-task barrier or serious disclosure/loss risk. Confidence is separate from severity. No user study was performed.

## Journey and existing strengths

| Journey | Current behavior and strengths | Evidence |
| --- | --- | --- |
| Browser -> queue | The browser is the real entry point. Empty-state copy explains it without inventing a host URL input. Queued tasks can be paused/cancelled. | S, R |
| Scan progress | Clean titles, separate quality, status words + glyphs, percentages, tabular numeric layout, speed/ETA and stable order already exist. Unknown-length work uses indeterminate progress. | S, R |
| Pause/resume | Resume has a visible text action; cancellation is available while active or paused. Pause retains resumable state; cancellation cleans up unfinished files. | S; presentation R, actual commands not run |
| Complete -> saved result | Show in folder is prominent. Play falls back to the folder when a file is unavailable. Removing a list entry preserves media files. | S, R; filesystem actions not run |
| Failure -> recovery | Human-readable problem + next step, Retry, selectable technical details and copy controls. Logs are secondary, not mandatory reading. | S, R |
| Limits | Existing inline disclosure, units/examples and a visible save action; no advanced-settings wall. | S, R |
| Return from tray | Real startup wires tray/show behavior and hides on close; custom hide button has an accessible label and explanatory tooltip. | S only |

Retention matters: `CancelDownload` performs cleanup; `RemoveDownload` removes the record without media-file deletion. Bulk-cancel cleanup protects settled outputs. Preserve these differences. See `MainViewModel.cs:975–1118`; no cleanup or retention operation was executed.

## Prioritized findings

| ID / journey | Observation and evidence | Consequence / priority | Proposed change and verification |
| --- | --- | --- | --- |
| A1 / incoming metadata | Unknown-title `TitleTooltip` returns `Task.UrlPayload`; the model explicitly says it contains encoded command + cookies. `DownloadItemViewModel.cs:61–65`, `DownloadTask.cs:34–38`. **S** | Potential UI disclosure of a reversible session-bearing handoff payload. **S3, high source confidence**; no real payload was displayed. | Use safe title/placeholder text only; do not decode/expose payloads for UI. Test synthetic sentinel payloads remain absent from tooltips/automation. Leave protocol and cookie handling untouched. |
| A2 / queue keyboard access | Forward Tab traversed toolbar, filters, limits, **first card only**, then Clear finished and back to the start. Later Resume/Show in folder/Retry were skipped. Programmatic traversal reproduced this with all four cards visible. List and containers are nonfocusable (`MainWindow.xaml:255–269,397–399`). **S, R** | Keyboard access to later tasks is blocked in this preview. **S3, high preview confidence**; full real-app keyboard flow remains untested. | Explicit queue/container navigation policy; make every task action reachable and scroll focused actions into view. Verify forward/reverse Tab, arrows as appropriate, Enter/Space, menu dismissal and focus retention across state changes. |
| A3 / native accessible identity | Seven actionable settings-menu peers have empty names; row peers report `YTDLPHost.ViewModels.DownloadItemViewModel`. Editable limits textbox peers also have empty names although parent comboboxes are named. **R**, backed by `MakeMenuItem` and XAML. | Weak task/control identification for assistive technology. **S2, high peer confidence**; not a Narrator result. | Name menu items explicitly, bind row names to safe titles and associate edit parts with their labels. Expose theme checked state, not just a visual check glyph. Inspect external UIA + Narrator before claiming success. |
| A4 / postprocessing | `IsProcessing` identifies conversion/embedding, but speed/ETA visibility binds only to `IsDownloading`. Converting/metadata runner branches do not clear those fields (`YtDlpRunner.cs:643–670`). A synthetic conversion card displays stale `3.30 MiB/s` and `00:00 elapsed`. **S, R** | Byte-transfer metrics can describe work that is not transferring bytes. **S2, high presentation confidence**, no live conversion tested. | Hide transfer metrics during processing while retaining honest phase and indeterminate progress. No fabricated processing percentage/ETA and no runner changes. Test all recognized processing phases with retained values. |
| A5 / filtering | Unselected count text has `Opacity=0.7` (`App.xaml:347`): **3.54:1 light**, **4.48:1 dark** after compositing. **S, R + measurement** | Below the 4.5:1 ordinary-text benchmark; do not round 4.48 to a pass. **S1, high measurement confidence** for default state. | Use an opaque semantic text brush rather than opacity-based dimming. Measure selected, unselected, hover and focus in both palettes. |
| A6 / unknown metadata | Queued unknown-title card says “Fetching video...” while its status says “In queue”. **S, R** | Implies extraction has begun when a task may only be waiting. **S1, high presentation confidence**. | Use a waiting/untitled placeholder until extraction actually begins, with no invented queue position. Test known/unknown titles across states. |
| A7 / attention summary | With no downloading/waiting/paused work, generic footer becomes “All downloads finished”, even if tasks failed. With processing, footer counts it as downloading. `MainWindow.xaml.cs:298–318`. **S**, footer illustration R is fixture-set. | Missing attention/phase distinction in the aggregate summary. **S1, medium impact confidence**. | Summarize real failures/processing separately using existing statuses. Preserve non-generic engine/update messages. Test all-failed, mixed, paused-only and processing-only queues. |
| A8 / large queues | 200 synthetic items generated **200 containers**. `CanContentScroll=False`; all-item realization matches Microsoft's documented virtualization risk. **S, R** | Higher rendering/memory cost is plausible. **S2 for large queues, high realization confidence**; slowness is **H**, not measured. | Separate performance slice: recycling/virtualization with item-owned expansion state. Measure scrolling and update latency; do not combine it with initial visual changes. |
| A9 / action scanning | Pause is glyph-only; cancel/remove share an X glyph with very different outcomes. They have useful names/tooltips already. **S, R**; confusion is **H**. | New users may need hover to identify their next action. **S1, medium confidence**. | Give the frequent per-state primary action a consistent labeled slot; keep destructive and overflow actions secondary and distinctly named. Validate with neutral find/pause/resume/locate tasks. |
| A10 / settings + motion | Invalid speed format opens a modal MessageBox and restores the saved speed; concurrency clamps to 1–15. Processing sweep and toast animation do not check the reduced-animation setting, although card entry does. **S** | Recovery is displaced from the field; animation handling is inconsistent. **S1, medium impact confidence**. | Defer inline validation to an approved settings-recovery slice; preserve accepted formats and live-application behavior. Add a stationary processing treatment when animations are disabled and test on Windows. |

### Contrast scope
Measurements use effective **drawing-element** solid brushes, text opacity and sRGB alpha compositing. Control `Background` properties ignored by their templates are not treated as painted backgrounds. These are not glyph-antialias measurements or a complete contrast audit.

| Default text treatment | Dark | Light |
| --- | ---: | ---: |
| Unselected filter count | 4.48:1 | 3.54:1 |
| Ordinary status text | 7.68:1 | 7.46:1 |
| Resume button text | 6.45:1 | 5.38:1 |
| Paused status text | 8.69:1 | 5.25:1 |

Main text colors do not need wholesale palette replacement. Opacity on counts is the specific measured issue. WCAG is a benchmark here, not a claim of native conformance.

## Verification record
- **Build passed:** host rebuilt through the temporary harness, 0 warnings/errors. No application/extension source changes.
- **Visually inspected:** [empty](ui-audit-evidence/01-empty-dark.png), [dark queue](ui-audit-evidence/02-queue-dark.png), [light queue](ui-audit-evidence/03-queue-light.png), [limits](ui-audit-evidence/04-limits-light.png), [minimum size](ui-audit-evidence/05-minimum-limits-light.png), [queued/conversion/live/cancelled](ui-audit-evidence/06-queued-processing-live-cancelled-dark.png), [error details + log](ui-audit-evidence/07-expanded-error-and-log-dark.png).
- **Limited native keyboard test:** one separate run sent real forward-Tab key events to the foreground preview at minimum size; [record](ui-audit-evidence/native-tab-minimum-confirmed-separate-run.json). It skipped later-card actions. In the final measurement run, foreground guards blocked input in both test states; no input was sent to another window. [Programmatic traversal](ui-audit-evidence/light-queue-focus-traversal.json) corroborates the default-size issue, but is not itself a physical-keyboard test.
- **WPF peer inspection:** [settings names](ui-audit-evidence/settings-menu-names.txt), [queue/limits peers](ui-audit-evidence/light-minimum-limits-automation-peers.json). Menu opened only; no setting, repair, disconnect, update or exit action invoked. This is not external UIA-tree or Narrator validation.
- **Text-pair measurement:** [dark](ui-audit-evidence/dark-queue-text-measurements.json), [light](ui-audit-evidence/light-queue-text-measurements.json); default states only.
- **Container realization:** [200-item record](ui-audit-evidence/large-queue-containers.txt); no responsiveness/throughput timing.
- **Not run:** live commands/downloads, settings validation interactions, zero filter results, real payloads, reverse-Tab/arrow/activation/dismissal workflows, focus-ring captures, Narrator, external UIA, real-app theme switching, system/contrast themes, reduced-motion changes, physical scaling/text enlargement, Snap, performance benchmarks or a logic test suite.

Runtime theme-switch fidelity remains unresolved; the harness uses fresh resources for static palettes. Neither a screenshot nor frozen parsed resources establishes production switching behavior.

## Recommended next slice
**A: Refined queue cards**, preceded by the small UI safety fix A1. Address keyboard/semantic identity, default count contrast and honest processing presentation before decoration. Defer virtualization, settings behavior and extension work. The [direction plan](UI-UX-DIRECTION.md) specifies alternatives, file boundaries and acceptance checks.
