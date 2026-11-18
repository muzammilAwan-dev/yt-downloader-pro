# Premium desktop materials — research and proposed slice

15 November 2026. **Historical research/proposal.** The owner subsequently approved the recommended upgrade plus softer geometry; current implementation and verification are tracked in [UI-UX-PREMIUM-IMPLEMENTATION.md](UI-UX-PREMIUM-IMPLEMENTATION.md). Desktop first; extension remains deferred. The proposal below is retained as research context, not a statement of current implementation status.

## Recommendation

**Native satin: a subtle Mica shell, readable opaque queue cards, restrained contours and quick interaction feedback.** The premium quality should come from well-resolved hierarchy and native behavior, not blur behind every label. Keep the existing blue accent, status vocabulary, card layout and system fonts.

Use Acrylic, if at all, on a separately verified transient surface later. Do not turn the main download list into a translucent desktop overlay.

## Evidence and interpretation

- **Rendered observation:** reopened the current Direction A [dark queue capture](ui-direction-a/02-queue-dark.png). Titles/actions are organized, full-width progress is prominent, and failure recovery is visible at default size. The mostly uniform opaque neutrals are deliberately quiet. This is actual WPF synthetic rendering on .NET 9, not a composited material preview or owner-preference test.
- **Source observation:** `App.xaml` and `ThemeService.cs` provide opaque canvas/card brushes, shared semantic colors and theme updates. `MainWindow.xaml.cs:ApplyTitleBarTheme` explicitly sets native caption/text/border colors. There is no backdrop material integration. Merely setting a DWM flag would leave opaque WPF backgrounds covering the result.
- **Design hypothesis:** a personalized base/chrome layer with solid cards could make the utility feel more finished without reducing scanability. The exact tint, exposed area and value to the owner need native comparison, not an HTML mockup.
- **Engineering hypothesis:** the existing HWND/DWM wiring is a reasonable place to prototype documented system-backdrop interop without a new toolkit. Actual WPF client compositing, title-bar integration, policy response and performance have not been established.

## What the effects really mean

| Treatment | Appropriate use here | Limits / decision |
| --- | --- | --- |
| **Mica** | Long-lived base/chrome, preferably visible in the title-bar area | Opaque, wallpaper-influenced material, **not see-through glass**. Designed to sample wallpaper once. Recommended first material to investigate. |
| **Mica Alt** | Stronger backdrop separation for a genuinely tabbed title bar | The queue filters are not document/title-bar tabs. No justification to choose Alt merely to amplify tint. |
| **Desktop Acrylic** | Temporary menus/flyouts that retain context | Real frosted translucency; may reveal other windows behind the app. GPU/power and legibility costs. Microsoft advises against large main-background use and accent-colored text over it. |
| **In-app Acrylic** | A temporary pane overlapping app content | Different from desktop blur. This app's inline limits panel does not currently overlap the queue; keep it solid. Not a built-in WPF `AcrylicBrush`. |
| **Layering + contours** | Base, cards, controls, failure recovery and transient surfaces | Most dependable premium treatment across supported Windows versions. Use subtle, theme-specific 1-DIP decorative contours; keep meaningful control/focus boundaries separately measurable. |
| **Shadows** | Native outer window and genuinely overlapping menus/dialogs | Do not apply expensive `DropShadowEffect` to every updating card or text container. WinUI `ThemeShadow` values are not WPF blur radii. Retain native outer-window behavior. |
| **Gradient / highlight** | At most a barely perceptible decorative surface edge | Optional project choice, not a platform requirement. No animated gradient, accent glow, text gradient or highlight that competes with progress. Existing solid semantic colors remain. |
| **Motion** | Brief hover/pressed feedback, deliberate disclosure/toast transitions | Start around 80–120 ms for simple feedback and 160–200 ms for a small disclosure, then test. These are candidate project timings, not universal rules. No row lift, queue reorder, progress overshoot or pulsing completion. Respect Windows animation preference. |

Mica/Acrylic guidance describes platform-managed fallbacks for transparency-off, high contrast, battery saver and unsupported contexts. **Custom WPF interop does not inherit a conformance or policy-handling guarantee:** verify behavior and supply solid fallback paths. Material API success is not proof it is visible.

## Two focused directions

These are material placements, **not rendered after-images**. Neither changes content, features or commands.

### A — Native satin (recommended)

```text
[Native caption + quiet Mica base, where integration permits]
YT Downloader Pro                              Limits   Hide   Settings
All 4   Active 2   Completed 1   Failed 1

[Opaque card]
↓ A walk through the mountains at sunrise                         1080p
  Downloading video · 42.6%    3.30 MiB/s · ETA 00:22   [Pause] [Stop] …
  ━━━━━━━━━━━━━━━━━━━───────────────────────────────────────────────
[Opaque recovery card]
! The site refused the download (HTTP 403)                     [Retry]
  Update the download engine in Settings and tools, then retry.
  Show details   Copy error
```

**Tradeoff:** personalized, Windows-native polish with stable text backgrounds. The Mica effect is deliberately subtle and only available through this proposed DWM path on newer Windows. Native client/title-bar compositing must be proved before committing. Older systems retain the existing solid treatment. Keep the native caption; do not introduce custom drag/resize/Snap obligations to obtain a screenshot aesthetic.

### B — Precision satin, fully opaque

Same real queue and actions, using a slightly more distinct opaque shell/content/elevated-surface hierarchy, crisp contours, disciplined geometry and brief control feedback. No wallpaper-dependent treatment. A frosted settings flyout could be considered **as a separate later experiment**, not a requirement of B.

**Tradeoff:** consistent and easier to measure across supported Windows, but without genuine glass personalization. More of the quality depends on subtle palette/geometry decisions rather than material. This is also the visual fallback target for A; unsupported Windows should not look like an unfinished product.

**Not recommended:** full-window Desktop Acrylic, translucent download/error cards, stacked glass panes, neon status glows, a borderless custom window, parallax/3D-hover cards or a framework migration for appearance alone.

## Compact semantic plan

Preserve current resource names and in-place theme updates. Add roles only where a distinct responsibility exists; exact values await rendered comparison.

| Role | Existing / proposed mapping |
| --- | --- |
| Solid canvas/fallback | Keep `BackgroundBrush` opaque in both palettes. Never globally make it transparent: it also feeds native caption coloring and fallback behavior. |
| Exposed material base | Candidate `WindowBaseBrush`: transparent **only in a verified material-enabled path**; otherwise the solid fallback. Do not reduce opacity on the entire window. |
| Download cards | Keep `SurfaceBrush` opaque initially, including failure/log text backgrounds. This conservatively differs from WinUI's low-opacity content-layer examples. |
| Elevated/supporting surface | Candidate `ElevatedSurfaceBrush` for existing transient/supporting UI if actually needed. Both palette entries and reduced-effect/contrast fallback required. |
| Surface contour | Preserve `BorderBrush`, `BorderStrongBrush`, `HairlineBrush`; evaluate light/dark edges separately. Decorative edges are not replacements for focus or action outlines. |
| Status and focus | Preserve existing accent/on-accent/success/warning/error and `FocusRingStyle`. No reduction in foreground opacity to make the design feel soft. |
| Type/spacing/geometry | Existing Segoe UI and 4/8/12/16/24-DIP rhythm. Keep 8-DIP cards and 4-DIP controls for the first experiment; do not also change density/identity. |
| Motion | Central timings/conditions for any new interaction animation; zero-duration/stationary alternatives. Preserve phase honesty and current reduced-motion processing branch. |

## WPF implementation tips and traps

1. **No toolkit required for the first investigation.** After HWND creation, the documented `DWMWA_SYSTEMBACKDROP_TYPE` is attribute **38**. `DWMSBT_MAINWINDOW=2` corresponds to Mica on Windows 11; `DWMSBT_TRANSIENTWINDOW=3` corresponds to Desktop Acrylic. These mappings may evolve with Windows. The documented minimum for this API is **Windows 11 build 22621**, even though other Mica implementations can support build 22000.
2. Gate the optional interop by OS support and policy; check each HRESULT. On unavailable API, failure or disabled effects, keep a solid native window. Do not use undocumented attribute 1029 or `SetWindowCompositionAttribute` recipes as a compatibility strategy.
3. Reconcile the forced caption color in `ApplyTitleBarTheme`. `DWMWA_COLOR_DEFAULT=0xFFFFFFFF` restores the system caption behavior; whether it produces the desired native material must be captured and checked. Restore current explicit colors on the solid fallback path. Do not conflate `COLOR_DEFAULT` with a transparent WPF brush.
4. A normal WPF window with a transparent `Background` is not automatically see-through. Investigate the HWND composition target and documented frame-extension integration in an isolated native prototype. Make **only** the intended base/chrome drawing path expose the DWM backdrop; keep cards opaque. Do not commit a collection of transparency flags without composited evidence.
5. Keep `AllowsTransparency=False` and the native frame for this scope. `AllowsTransparency=True` is for layered/nonrectangular windows and requires `WindowStyle=None`; it is not a harmless glass switch.
6. `BlurEffect` blurs its target texture, including child text if placed on a container. It does **not** sample the desktop behind the WPF window. Do not blur the queue to simulate acrylic, or screenshot unrelated desktop content to construct a fake backdrop.
7. Old `DwmEnableBlurBehindWindow` Aero examples do not establish modern blur: Microsoft explicitly says this call no longer produces blur beginning with Windows 8.
8. WPF `ContextMenu`/`Popup` has its own HWND/compositing lifecycle. A parent-window backdrop does not make it acrylic. Verify popup window styles, alpha, DWM applicability, menu semantics, dismissal/focus and fallbacks separately. Keep the existing solid menu until that experiment succeeds.
9. WinUI `Window.SystemBackdrop`, `AcrylicBrush`, `ThemeShadow`, `MicaController` and `NavigationView` examples are not drop-in WPF APIs. The Windows App SDK Win32 composition approach adds dependencies/deployment work; it is an alternative to evaluate only if the scoped DWM approach proves inadequate and the owner approves that cost.
10. Test actual theme changes. Existing static-palette previews parse/freeze brushes, so they cannot prove production resource updates or material theme alignment. Neither the proposed API nor temporary .NET installation fixes that limitation.

## Proposed next slice — approval required

**One isolated native A-vs-solid prototype**, with synthetic queue content and no engine, protocol, settings/history persistence or clipboard actions. No production app material edits yet.

- Scope: base/native caption integration and fallback only. Leave queue geometry, bindings, commands, controls and card backgrounds unchanged. No Acrylic popup in this first slice.
- Possible production files after approval: `MainWindow.xaml.cs` (native material lifecycle and checked fallback), `MainWindow.xaml` (exposed base path), `App.xaml` + `ThemeService.cs` (new semantic base/fallback role). A small material service is reasonable only if native lifecycle/policy handling merits separation; no new package is implied.
- Capture the **actual composited selected app window**, using synthetic surrounding content if background variation is needed. Same size/DPI/theme/data for solid and material; exclude unrelated desktop/private content. `RenderTargetBitmap` client captures alone cannot prove a DWM effect.
- Exercise active/inactive, transparency off, Windows contrast theme, light/dark/system changes, supported and older-OS fallback, resize/maximize/restore/Snap, 100/150/200% DPI and reduced motion. Any unavailable environment remains explicitly not run.
- Measure text/control/focus contrast on the resulting backgrounds, including hover/pressed/focus. Re-run 174 presentation checks and native keyboard/UIA checks; add Narrator/manual critical flows. Measure scrolling/update cost with representative queues before claiming negligible overhead.
- Promote only if the material is visible, fallback is complete, readability/native behavior is preserved and the owner prefers the native comparison. Otherwise choose B, not more blur.

## Authoritative research

Reviewed 15 November 2026; platform guidance and API facts are distinct from the project choices above.

- [Microsoft: Mica](https://learn.microsoft.com/en-us/windows/apps/design/style/mica) — opaque long-lived base, layering and fallbacks.
- [Microsoft: Acrylic](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic) — transient use, blend types, GPU cost and legibility cautions.
- [Microsoft: DWM system backdrop type](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type) and [window attributes](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute) — interop values, caption reset and minimum builds.
- [Microsoft: layering/elevation](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/layering) and [motion](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/motion) — hierarchy, purposeful shadows and context-specific transitions.
- [Microsoft: Win32 Mica with Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-mica-win32) — alternative controller/composition route, with dependencies.
- [WPF: AllowsTransparency](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.allowstransparency?view=windowsdesktop-8.0), [BlurEffect](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.effects.blureffect?view=windowsdesktop-8.0), [DwmEnableBlurBehindWindow](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmenableblurbehindwindow) — transparency/blur constraints, not a material implementation recipe.

No community package, font, sample app or external executable was installed for this research. Owner-authorized temporary .NET runtime testing is recorded separately in [UI-UX-NET8-VERIFICATION.md](UI-UX-NET8-VERIFICATION.md).
