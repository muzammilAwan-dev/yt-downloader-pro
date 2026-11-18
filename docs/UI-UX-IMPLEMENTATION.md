# Direction A — desktop implementation and review

15 November 2026. Owner approved the recommended **refined queue cards** direction. This completes that focused implementation slice, not every item in the product acceptance checklist.

## Delivered
- Full-width titles and quality, with aligned status/metrics and a labeled main action below. Pause is now recognizable without hovering. Cancel uses a stop glyph, distinct from list removal; original commands and retention explanations remain.
- Consistent card spacing, readable wrapped status text and a less bulky recovery panel. The default four-task fixture fits with all recovery links visible in both palettes; limits/minimum-size queues still scroll normally.
- Tooltips use safe title text only, never the session-bearing handoff payload. Waiting/settled unknown titles say “Untitled download”.
- Processing suppresses stale transfer speed/ETA/percentage and uses an indeterminate presentation. Live-transfer metrics and paused fills remain.
- Explicit forward/reverse queue traversal, focus-aware scrolling, focus recovery when a task action is replaced, and settings/card-menu focus restoration. Window deactivation no longer clears focus indiscriminately.
- Named task rows, progress controls, editable limits fields, menu actions and diagnostics. Theme choices now have native checkable/checked state; visible checks retained.
- Opaque filter counts, larger diagnostic-link hit areas, an action pressed state, and stationary processing/toast handling when Windows animation is disabled.

### Application files changed
`host/YTDLPHost/MainWindow.xaml`, `host/YTDLPHost/MainWindow.xaml.cs`, `host/YTDLPHost/App.xaml`, `host/YTDLPHost/ViewModels/DownloadItemViewModel.cs`.

No download-engine, validation, protocol, cookie, settings/history persistence, file cleanup or extension source edits. No new package, UI framework, font, SDK/runtime installation, Git initialization or publishing in this implementation slice. `ThemeService` and its palettes remain unchanged. A later owner-authorized isolated .NET 8 runtime test was completed and removed; see [verification and cleanup](UI-UX-NET8-VERIFICATION.md).

Added dependency-free checks in [`tests/YTDLPHost.UiChecks`](../tests/YTDLPHost.UiChecks/README.md). Documentation/evidence changes are local.

## Native before/after review

Actual compiled WPF window and task view models, not an HTML mockup. Source `App.xaml` resources are parsed and initialized from the real palettes. Normal app/root-view-model startup is bypassed; synthetic pause/resume changes fixture status only, other root commands are no-ops. Filter/count/footer content is fixture-controlled.

Before/after use the same direct-content/background render method, fixture, sizes and static palettes. Host DPI is 144; output is 96-DPI client-area rendering, **not physical 150% screenshots**. Native title bars are excluded. Preview runs on explicitly rolled-forward .NET 9.0.8 because Windows Desktop 8 is absent. Intended-runtime/startup parity remains unverified.

**Later verification:** the delivered source also passed 174 checks and seven native synthetic-state inspections on temporary Windows Desktop **8.0.34**, without major roll-forward. [Additional evidence](UI-UX-NET8-VERIFICATION.md). Original before/after captures above remain .NET 9; normal startup/production parity and runtime theme switching still unverified.

| State | Before | After |
| --- | --- | --- |
| Empty / dark | [Before](ui-audit-evidence/01-empty-dark.png) | [After](ui-direction-a/01-empty-dark.png) |
| Queue / dark | [Before](ui-audit-evidence/02-queue-dark.png) | [After](ui-direction-a/02-queue-dark.png) |
| Queue / light | [Before](ui-audit-evidence/03-queue-light.png) | [After](ui-direction-a/03-queue-light.png) |
| Limits / light | [Before](ui-audit-evidence/04-limits-light.png) | [After](ui-direction-a/04-limits-light.png) |
| Minimum + limits | [Before](ui-audit-evidence/05-minimum-limits-light.png) | [After](ui-direction-a/05-minimum-limits-light.png) |
| Queued / processing / live / cancelled | [Before](ui-audit-evidence/06-queued-processing-live-cancelled-dark.png) | [After](ui-direction-a/06-queued-processing-live-cancelled-dark.png) |
| Expanded error + log | [Before](ui-audit-evidence/07-expanded-error-and-log-dark.png) | [After](ui-direction-a/07-expanded-error-and-log-dark.png) |

All seven final after-images were opened and visually inspected. First-pass excess card height was corrected before delivery. Final defaults are 980×700 DIPs; minimum 840×540. Below-viewport items are expected at minimum size or with limits expanded; new traversal reaches their actions.

## Verification matrix

| Check | Environment / evidence | Result / limit |
| --- | --- | --- |
| Host build | [Final build](ui-direction-a/direction-a-host-build.txt), requested Debug solution command | **Pass:** 0 warnings, 0 errors. |
| Presentation regressions | [174-check result](ui-direction-a/direction-a-presentation-checks.txt), real item view model on .NET 9 Desktop | **Pass:** payload sentinel exclusion, unknown titles, five processing phases with stale metrics, transfer/live/paused presentation, existing action availability. No live command/retention test. |
| Render inspection | Seven matched synthetic WPF states above | **Reviewed:** light/dark, empty, waiting, transfer, conversion, paused, completed, failed, cancelled, long titles, missing metadata, limits and expanded diagnostics. |
| Queue focus traversal | [Forward](ui-direction-a/light-queue-focus-traversal.json), [reverse](ui-direction-a/light-queue-reverse-focus-traversal.json), [minimum forward](ui-direction-a/light-minimum-limits-focus-traversal.json), [minimum reverse](ui-direction-a/light-minimum-limits-reverse-focus-traversal.json) | **Pass, programmatic only:** all four task cards reached in both directions, default light/dark and minimum/limits. Not a physical-keyboard test. |
| Focus/state/disclosure | [Native-control checks](ui-direction-a/native-control-checks.json) | **Pass, programmatic synthetic controls:** Pause -> Resume focus, settings dismissal restoration, technical-details expansion with retained focus. Real download command not run. |
| Physical keyboard | [Dark](ui-direction-a/dark-queue-native-tab-input.json), [minimum](ui-direction-a/light-minimum-limits-native-tab-input.json) | **Blocked:** foreground guards prevented input; no keys sent to unrelated windows. Real Tab/Shift+Tab, arrows, Enter/Space, Escape and focus-ring appearance remain to test. |
| External UI Automation | [Selected-window result](ui-direction-a/external-uia-result.txt), [98 elements](ui-direction-a/external-uia-window.json) | **Inspected:** actual task names, named limits edit controls and named progress with range patterns. No controls invoked; Narrator not run. |
| Settings menu peers | [Menu names](ui-direction-a/settings-menu-names.txt) | **Pass for names:** previously unnamed actions now report actual labels. Menu opened only; no theme, repair, update, disconnect or exit choice invoked. Native checked state is implemented but not externally exercised. |
| Default text contrast | [Dark](ui-direction-a/dark-queue-text-measurements.json), [light](ui-direction-a/light-queue-text-measurements.json) | **Pass for sampled default solid-brush text pairs:** no ordinary-font pair below 4.5:1 in default light/dark or minimum/limits. Counts: **7.68:1 dark, 7.46:1 light**, previously 4.48/3.54. Effective painted brushes + alpha; not glyph/pixel or complete accessibility certification. |
| Reduced motion branch | [Injected template condition](ui-direction-a/native-control-checks.json) | **Pass for injected `Tag=false`:** sweep collapses while progress stays indeterminate. Actual Windows animation setting was **True**; no OS preference changed. Stationary branch is not a full Windows-settings-change test. Toast behavior not rendered/tested. |
| Large queue | [Realization record](ui-direction-a/large-queue-containers.txt) | **Unchanged:** 200/200 containers generated, virtualization still deferred. No performance benchmark/improvement claim. |

## Remaining production validation
- Temporary .NET 8 Desktop synthetic testing is now completed with owner authorization and cleanup. Safe real-app startup and runtime dark/light/system switching remain to verify; frozen parsed-resource behavior is not proof of production theme-switch behavior.
- Physical keyboard critical flows, visible focus, offscreen focus, filtered-out/removed tasks, card-menu dismissal, tray return and command-driven state transitions.
- Narrator, Windows contrast themes, actual reduced-motion changes, hover/pressed/focus visual measurements, 100/150/200% display scale, enlarged text and Snap.
- Live downloads/processing/retry, pause/cancel/remove/file retention, logs during active updates, zero filter results, settings validation and long paths. Source contracts were preserved, not live-tested.

Footer attention summary, virtualization/performance and inline settings recovery remain separately scoped audit items. The failed-item footer in synthetic images is fixture text, not evidence that aggregate summary behavior was fixed. Extension implementation remains deferred.

Evidence context: [preview method](ui-direction-a/preview-context.txt). The [retained preview source](ui-direction-a/preview-source/Program.cs) is an evidence artifact with this workspace's absolute reference paths, not an installed utility or portable production entry point. Repeatable presentation checks live in `tests/`.
