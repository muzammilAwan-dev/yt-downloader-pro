# Native satin + softer geometry — implementation record

15 November 2026. Owner approved applying the recommended upgrades and asked for moderately rounder, less boxed surfaces. **Implemented locally; native synthetic verification completed with the limits below.** Temporary .NET 8 runtime/download/test files were removed; an empty locked evidence directory and its parents remain. Nothing was committed or published; extension work remains deferred.

## Focused direction

- Keep the existing queue-card journey: scan title/state, Pause/Resume/Retry, inspect recovery, reveal logs and locate the saved file. No new navigation, features or queue reordering.
- Use a consistent **12-DIP card/flyout radius, 8-DIP controls and inset panels, 10-DIP keyboard focus-ring radius**. These are design choices, not accessibility standards. Keep the slim progress track round to its own height; no full-pill buttons or balloon-like cards.
- De-emphasize decorative card outlines without removing useful hover/focus/error cues. Separate decorative card contours from semantic control/focus brushes. Preserve default window size, task-card content and action heights.
- Preferred material: documented Mica on the long-lived native shell, opaque task/log/recovery surfaces and a complete solid fallback. Keep the native frame, not a borderless replacement. Acrylic popup integration remains deferred.
- Motion, if added: short control feedback only, stationary when Windows disables animation. No blurred text, per-card shadows, row lift or fake progress.

## Observation / approval boundary

The prior material document compared Native satin against fully opaque Precision satin. The owner selected the recommendation and extended its geometry scope. Source observations: current cards have 8-DIP corners, controls mostly 4 DIPs, and shared generic border brushes give many independent rectangular outlines. Current-source dark/light baseline was rebuilt with isolated .NET 8.0.34 and inspected before app edits; repository images remain excluded. Rendered observation: secondary outlined actions nested within outlined cards amplify the boxed appearance; state/title/action hierarchy is already clear and should remain unchanged.

The geometry/solid refinement was approved for implementation. The material promotion boundary required real composited WPF evidence, native framing and checked failure paths, not just a DWM HRESULT or client `RenderTargetBitmap`. The earlier manual probe showed a subtle light-shell tint and solid restoration. **Final service captures do not confirm a visibly distinct material:** the fresh light Mica-on/solid pair is nearly identical. The integrated service is therefore a best-effort native enhancement, not a verified visual-material acceptance pass. Native API failure injection and the real Windows preference matrix remain open.

## Implemented slice

- `host/YTDLPHost/App.xaml`: shared geometry tokens, rounded focus borders, quiet filled secondary queue actions; no pill buttons, per-card blur/shadows or new continuous animation.
- `host/YTDLPHost/MainWindow.xaml`: softer opaque cards, filter track, limits, logs, recovery panels and toast. Decorative `CardContourBrush` is distinct from meaningful hover/focus/error strokes.
- `host/YTDLPHost/Services/ThemeService.cs`: contour token in both palettes; existing mutable-brush updates and frozen-brush replacement preserved.
- Theme-brush consumers in the two XAML files now use `DynamicResource`; styles, converters, glyphs and geometry remain static. A safe compiled-resource test reproduced a frozen dark header after light switching (`MatchesPalette=false`). After the fix, its visible foreground matches the replacement light brush (`#FF1A1A1A`, `MatchesPalette=true`). This is an evidence-based theme fix, not blanket resource conversion.
- `host/YTDLPHost/Services/WindowMaterialService.cs` and `MainWindow.xaml.cs`: documented Windows 11 22H2+ shell Mica, opaque queue content, native caption/resize frame retained. Policy excludes high contrast, transparency-off, saver/unreadable power policy, explicit software rendering and unavailable composition; older Windows retains a solid shell. Enable HRESULTs are checked; fallback paints opaque WPF content before native rollback. Preference/power subscriptions, HWND hooks and notification handles have disposal paths.
- `tests/YTDLPHost.UiChecks/Program.cs`: 128 pure material-policy combinations added to the existing 174 presentation checks.

No packages/framework migration, identity change or new navigation. The **68 existing MainWindow binding expressions are an identical multiset before/after**; this source comparison is not a live behavior test. Download view-model/engine/protocol/cookie/persistence contracts and extension source were not edited in this slice.

## Native evidence

See [evidence guide](ui-premium/README.md) for retained harness source and intermediate results. The current-source baseline was built and inspected before premium edits; outdated repository screenshots were not substituted.

| State | Before | Final solid WPF |
| --- | --- | --- |
| Dark queue | [Baseline](ui-premium/evidence/before/02-queue-dark.png) | [Softer queue](ui-premium/evidence/solid-final/02-queue-dark.png) |
| Light queue | [Baseline](ui-premium/evidence/before/03-queue-light.png) | [Softer queue](ui-premium/evidence/solid-final/03-queue-light.png) |

Matched solid images use identical synthetic task data, client size and 96-DPI bitmap rendering on a 144-DPI host. For these bitmap captures only, the preview restores/disposes the material service so queued Windows messages cannot re-enable transparency. Earlier `evidence/after/` bitmaps are **invalid material/solid comparisons** and retained only as intermediate evidence.

Final solid states inspected: [empty](ui-premium/evidence/solid-final/01-empty-dark.png), queues above, [limits](ui-premium/evidence/solid-final/04-limits-light.png), [minimum + limits](ui-premium/evidence/solid-final/05-minimum-limits-light.png), [queued/processing/live/cancelled](ui-premium/evidence/solid-final/06-queued-processing-live-cancelled-dark.png), and [expanded error/logs](ui-premium/evidence/solid-final/07-expanded-error-and-log-dark.png).

Real composited selected-window crops include the native caption but exclude outer corners/borders and unrelated desktop content. They use physical pixels at 144 DPI, with the default preview fitted to **980×656 DIPs** rather than 980×700 because of the available work area; minimum is **840×540**. [Keyboard focus](ui-premium/evidence/product-final/07-limits-keyboard-focus-light.png) and [minimum](ui-premium/evidence/product-final/06-minimum-light.png) were inspected. Capture guards checked visibility, screen bounds and Z-order before/after; evidence-only topmost and temporary activation were not added to the product.

**Material qualification:** [fresh Mica-on](ui-premium/evidence/product-final-comparison/08-fresh-light-mica.png) versus [fresh solid](ui-premium/evidence/product-final-comparison/09-fresh-light-solid.png) records backdrop type 2/transparent WPF base versus type 1/opaque base, both active and unobstructed. These observations establish native configuration and fallback, not visible wallpaper influence. Earlier [manual probe](ui-premium/evidence/material-probe-activation/05-mica-light.png) tint must not be promoted to final service proof.

## Verification status

| Check | Environment / evidence | Result / limit |
| --- | --- | --- |
| Final host build | `dotnet build host/YTDLPHost.sln --configuration Debug --nologo`; [log](ui-premium/evidence/host-final-build.txt) | Passed: 0 warnings, 0 errors. Check/harness builds also succeeded; no retargeting. |
| Presentation/policy regression | Isolated .NET 8.0.34; [results](ui-premium/evidence/checks-final-results.txt) | Passed: **174 presentation checks + 128 material-policy combinations**. Policy truth table is not native failure injection. |
| Native states | Seven final solid captures above | Inspected empty, queued, active, indeterminate/processing, paused, completed, failed, cancelled, long title, limits/minimum and expanded logs. Not a live task/engine test. |
| Runtime themes / software fallback | [Final service results](ui-premium/evidence/product-final/verification-results.json) | Light/dark/system mode calls exercised; frozen-brush replacement visibly corrected. Explicit software rendering produced opaque light/dark fallback. Real OS preference changes not exercised. |
| Real keyboard input | `product-final` results; exact-HWND foreground guard; programmatic initial focus | 23-step Tab and 23-step Shift+Tab returned to start; Space opened limits, Tab reached its editable `Downloads at once` TextBox, Space Pause/Resume changed synthetic task state and restored corresponding action focus; settings Space/Escape restored focus. No clipboard/remove/cancel/tray/tool/save action invoked. |
| Later comparison keyboard attempt | [Comparison results](ui-premium/evidence/product-final-comparison/verification-results.json) | **Blocked/incomplete:** foreground guard refused 9 input attempts. Its action result labels are not proof of execution. Keyboard pass claims refer only to the earlier `product-final` run (0 guard blocks). |
| Native lifecycle/windowing | Final service results | Programmatic maximize/restore retained caption/resize styles; theme change after closed/disposed HWND completed without exception. Not manual Snap/system-menu coverage. |
| External UI Automation | Process/HWND-scoped reader; [result](ui-premium/evidence/solid-final/external-uia-result.txt) | Exit 0, **98 elements**, no controls invoked. Not Narrator or announcement verification. |
| Sampled ordinary-text contrast | [Summary](ui-premium/evidence/verification-summary.json), 136 samples across five states | Minimum **4.93:1** in light / **5.11:1** in dark; no sampled ordinary text below 4.5:1. Effective solid drawing brushes with alpha composition, not glyph pixels or DWM shell; hover/selection/focus and full meaningful-UI contrast not measured. No accessibility-conformance claim. |
| Large queue | Before/after realization records in summary | **200/200** item containers, `CanContentScroll=false`, unchanged. No performance timing, virtualization improvement or throughput claim. |
| Reduced animation branch | [Synthetic control checks](ui-premium/evidence/solid-final/native-control-checks.json) | Injected existing `Tag=false` branch hid sweep while retaining indeterminate state. Not a real Windows reduced-animation preference test. |
| Source preservation | [Manifest](ui-premium/preservation-manifest.json) | **219 exact files**, SHA-256 verified against originals before removal and independently verified after cleanup. Runtime ZIPs, runtime tree and `bin/obj` excluded. |

### Not exercised / remaining acceptance work

Normal startup/installed-product flow, protocol payloads, history/settings persistence, real download/retry/pause/cancel/file actions, engine updates and cookie cleanup were deliberately not run. The harness uses synthetic root data and actual built window/item view models; its safe application compiles source resources without production startup/exit. Synthetic parity does not establish production behavior.

Still open: visibly distinct **final-service** material on a representative machine; native API-failure injection; supported older-Windows verification; actual Windows contrast/transparency/saver changes; Narrator/live announcements; full hover/selected/focus/composited contrast; 100/150/200% display-scale matrix and enlarged text; manual Snap/title-bar/system-menu checks; missing-metadata/path stress, zero filter results and settings validation in the final premium slice; large-queue scrolling/performance. A synthetic capture/build is not an accessibility or release acceptance pass.

## Runtime isolation, preservation and cleanup

- Reused the owner's temporary-test authorization for a **second**, ZIP-only .NET 8.0.34 Core + Windows Desktop runtime tree. Both official archives were verified against published SHA-512 values; [download evidence](ui-premium/evidence/runtime-download-verification.json). Used explicit isolated `runtime/dotnet.exe`; no installer, elevation, PATH/DOTNET_ROOT changes or system-wide Windows Desktop 8 installation.
- Preserved current/baseline captures, intermediate failure evidence, exact harness source and before/after source snapshots in `docs/ui-premium/` (about 4.45 MiB). Every copied file matched SHA-256; [verification script](ui-premium/verification/verify-preservation.py) remains runnable without .NET 8.
- [Cleanup record](ui-premium/cleanup-verification.json): no matching process before removal; **runtime directory, downloaded ZIPs and all temporary test files removed**. System runtime list matches the earlier baseline; Windows Desktop 8 remains absent. Windows refused deletion of the empty `C:\Users\Malku\AppData\Local\Temp\opencode\ytdlp-premium-20261115\evidence\before` directory due to an external lock; its empty parents remain, **0 files**. No lock-owning process was terminated. Full temporary-directory removal is therefore not claimed.
- .NET 8 reached end of support on **10 November 2026** per the retained official metadata. Framework/runtime migration is separate from this visual upgrade; the app target was not silently changed.
