# Graphite + porcelain + blue — approved palette slice

15 November 2026. Owner approved the recommended palette with “ok apply professionally and properly”. **Palette implementation and scoped native verification completed; temporary runtime/download/test files removed.** Two externally locked empty evidence directories and their parents remain. Desktop only; no layout, logo, extension, engine, persistence or material-policy changes. All work remains local and unpublished.

## Direction and real journey

Keep scanning task title/status/progress, choosing Pause/Resume/Retry, reading recovery and locating saved files unchanged. Blue remains active progress, links, focus and primary next actions; sage means success, amber pause/attention, coral/brick failure. Status text/glyphs remain; color is not the only cue. Secondary actions and selected filters stay neutral.

Compared directions: approved **cool graphite + porcelain + blue** retains Windows utility familiarity; warm charcoal + ivory + teal was a larger identity departure and was not selected. The signature stays the well-resolved progress/status treatment, not decorative tinted panels.

**Source observations:** tokens already live in `App.xaml`/both `ThemeService` palettes; dynamic consumers support frozen-brush replacement. Rounded surfaces and existing density are retained. **Rendered observations:** fresh built native dark/light baseline was inspected before app edits; neutral dark grays felt flat, and brighter status hues competed with primary blue. Final native captures show subtler cool surface steps and calmer status colors without changing title/state/action hierarchy. **Hypothesis:** this makes queue scanning less visually noisy; no usability study or owner preference test establishes improved task completion.

## Applied semantic palette

| Role | Dark | Light |
| --- | --- | --- |
| Canvas | `#181A1E` | `#F2F3F5` |
| Opaque card/surface | `#22252A` | `#FCFCFD` |
| Hover/inset | `#2C3037` | `#E9ECF0` |
| Secondary pressed | `#3B424D` | `#DCE1E8` |
| Primary text | `#F2F4F7` | `#20242A` |
| Secondary text | `#B8BEC9` | `#545C69` |
| Muted text | `#9EA6B3` | `#5F6976` |
| Accent | `#7BAAF7` | `#2463C4` |
| Accent hover | `#94BAFA` | `#1D53A8` |
| Accent pressed | `#6D9CE9` | `#19498F` |
| On accent | `#111C2E` | `#FFFFFF` |
| Success | `#86C89A` | `#247343` |
| Pause/attention | `#E3B86C` | `#8B5E18` |
| Error | `#F3949D` | `#AF3541` |

Extended existing semantic palettes for coherent tracks, logs, borders and alpha tints; retained existing semantic names and alpha strengths. Color values are product design choices, not standards.

Calculation found the proposed light-blue action with the existing whole-button pressed opacity (`0.85`) drops white-text contrast below 4.5:1. Added shared `AccentPressedBrush` / `SurfacePressedBrush` and solid pressed colors for primary/secondary actions rather than fading their labels. This is a focused palette-state correction, not a layout/behavior redesign. Bindings, command availability, geometry, timing and native windowing remain unchanged; only the two button templates gain named borders and pressed-fill setters. Existing disabled/icon-button treatments remain unchanged.

The first regression run also rejected initial light brick `#B93B46` over its subtle tint (4.22:1; its 75%-opacity pressed icon was about 2.94:1). Deepened it to `#AF3541`, retaining the intended brick hue with a safer text/icon margin. This refinement is within the approved palette direction.

Application files: `host/YTDLPHost/App.xaml`, `host/YTDLPHost/Services/ThemeService.cs`. Checks: new `tests/YTDLPHost.UiChecks/PaletteChecks.cs`, runner updates in `Program.cs`, and actual app defaults embedded by `YTDLPHost.UiChecks.csproj`. No dependencies/framework migration. `MainWindow.xaml` and `MainWindow.xaml.cs` remain byte-identical to the fresh baseline; all 10 `App.xaml` binding expressions match. Theme update mechanism, material service, download commands, security/persistence and extension were not edited.

## Native evidence and review

Retained [evidence guide](ui-palette/README.md), [verification summary](ui-palette/evidence/verification-summary.json), exact harness/source snapshots and SHA-256 manifest. Authoritative final previews: [dark queue](ui-palette/evidence/after/02-queue-dark.png) · [light queue](ui-palette/evidence/after/03-queue-light.png). Before: [dark](ui-palette/evidence/before/02-queue-dark.png) · [light](ui-palette/evidence/before/03-queue-light.png).

Seven matched captures cover empty, active/paused/completed/failed, long title, limits/default/minimum, queued/missing metadata, processing/unknown-duration live transfer, cancelled and expanded error/log. All before/after pixel dimensions match. Solid fixture loads the actual compiled `MainWindow` and source resources with real task/item view models; materials are restored/disposed for bitmap fidelity. Root queue/counts/commands are synthetic. Captures are 96-DPI client bitmaps on the 144-DPI host: 980×700 DIPs (966×663 bitmap); minimum 840×540 (826×503 bitmap). This is not a DPI matrix or production-startup test.

Rendered review: queue title/state/actions remain distinct in both themes; long titles wrap, failure recovery stays readable, logs retain their own inset surface. At minimum with limits open, fewer items fit and the queue scrolls; visible inputs/save/footer remain in the window. Composited focus captures show a blue focus boundary around the limits input and settings control. These captures retain a partially scrolled queue from keyboard traversal; they are not matched top-of-list geometry comparisons. No claim of complete focus visibility across every control/state.

## Checks actually run

| Check | Environment / evidence | Result and limit |
| --- | --- | --- |
| Host + checks + native fixtures build | .NET SDK, Debug; retained final build logs | **Pass**, 0 warnings / 0 errors for each final build |
| Presentation/material/palette regression | Isolated .NET 8.0.34 | **Pass: 174 + 128 + 236 = 538 checks**; not live download tests |
| Token text contrast/default consistency | Actual embedded `App.xaml` and both palettes, including alpha/pressed states | **Pass**; 56 text pairs/theme, minima dark 5.251:1 / light 4.596:1; 15 focus/progress pairs/theme, minima 4.311:1 / 4.369:1 |
| Effective rendered text brushes | 136 ordinary-text samples across five final solid states, alpha composited | **Pass sampled pairs**, minimum dark 5.68:1 / light 5.11:1; not antialiased glyph/DWM pixels or full conformance |
| Painted action/selected branches | Actual built templates; ten injected native captures + `state-results.json` | **Pass sampled brush pairs**, opaque labels; default/hover/pressed primary: dark 7.27 / 8.67 / 6.17:1, light 5.74 / 7.36 / 8.78:1 |
| Runtime theme replacement | Source resources compiled into safe application; dark→light, system theme, closed-window changes | **Pass exercised changes**; frozen brush replaced, visible header matches `#20242A`; no production startup |
| Real keyboard | Exact HWND/process/foreground guard, programmatic initial focus, synthetic root commands | **Pass exercised flow**, 23-step Tab and Shift+Tab cycles return to start; Space opens limits, Tab reaches input, Pause/Resume hands focus to replacement, settings Space/Escape restores focus; **0 guard blocks** |
| Native control/peer inspection | Programmatic details expansion/collapse and settings; external selected-window UIA | Recorded retained/restored focus; external UIA read **98 elements, exit 0**, no invocation; **not Narrator verification** |
| Native lifecycle/material fallback | 144-DPI host, 980×656 / minimum 840×540 DIPs; nine selected-window composited crops | Native maximize/restore and closed-window theme changes completed; opaque light/dark software-rendering fallback recorded. Configured Mica/DWM type 2 is **not visible-material confirmation** |
| Reduced-motion branch | Injected `Tag=false` | Sweep collapsed and indeterminate state retained; **actual Windows animation setting not changed** |
| Large queue realization | 200 synthetic items | **200/200 containers**, `CanContentScroll=false`, unchanged; no performance/throughput improvement claimed |

Injected hover checks need special qualification: reflected read-only property overrides caused expected hover paints, but recorded `IsMouseOver=false`; pressed recorded true. These prove selected template-branch colors/label opacity, **not physical pointer enter/press/release behavior**. State-fixture tasks remained Downloading/Paused/Completed/Error without commands.

## Remaining verification

**Not run:** production startup/exit, extension handoff, engine/download/update/file/cookie/history behavior; Narrator/live announcements; real Windows contrast themes/transparency/saver changes; physical pointer state transitions; 100/150/200% DPI and enlarged-text matrix; Snap; all menus/dialogs/disabled states; settings validation, zero filter results and long saved paths; large-queue scrolling/timing. Visible final-service Mica remains unconfirmed. These are separate follow-up checks, not implicit passes or reasons to broaden this palette change.

## Evidence preservation and temporary runtime

Used the previously owner-authorized official ZIP-only Core/Desktop .NET 8.0.34 runtime; both SHA-512 archive hashes checked against retained official metadata. No normal startup, system install or retargeting. .NET 8 support ended on 10 November 2026; framework migration is separate scope.

**134 exact evidence/harness/before/after-source files** retained under `docs/ui-palette/`, copy SHA-256/byte sizes verified against originals and independently rechecked. No runtime/download archives or build-output folders retained there. Recheck with `python docs/ui-palette/verification/verify-preservation.py`.

Final [cleanup record](ui-palette/cleanup-verification.json): no matching process before removal; isolated runtime and downloads absent; **0 temporary files remain**; system runtime list matches the pre-test baseline and Windows Desktop .NET 8 remains absent. The exact palette tree retains only externally locked empty `evidence/after` and `evidence/compiled-native`, plus their `evidence`/root parents. No unrelated process was terminated to clear them. The first partial removal is retained separately in `cleanup-initial-attempt.json`; a single continuation removed independent entries and recorded locks without a retry loop. Windows blocked the initial PowerShell script execution, so Python standard-library cleanup was used without execution-policy/environment changes. The earlier premium locked empty directory was not touched.
