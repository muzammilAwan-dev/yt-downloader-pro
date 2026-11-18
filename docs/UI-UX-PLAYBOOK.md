# UI/UX playbook

Research/setup date: 15 November 2026. Scope: **desktop app first; extension later**.

## Installed design skills
These are original, project-specific instruction skills synthesized from the sources in `docs/UI-UX-SOURCES.md`, not third-party packages or copied design-system components.

| Skill ID | Purpose |
| --- | --- |
| `ytdlp-ux-research` | Task analysis, heuristic audit, information architecture, evidence and usability validation |
| `ytdlp-design-system` | Art direction, visual hierarchy, semantic tokens, typography, spacing, theming and purposeful motion |
| `ytdlp-wpf-ui` | Native WPF/MVVM implementation, windowing, templates, bindings and UI performance |
| `ytdlp-accessibility` | Contrast, keyboard/focus, UI Automation, scaling and assistive-technology checks |
| `ytdlp-ux-writing` | Action labels, onboarding, status, errors, privacy explanations and destructive-action clarity |
| `ytdlp-extension-ui` | Compact extension popup/overlay UX, semantics, lifecycle and host-handoff feedback; deferred |

Skills live in `.opencode/skills/<id>/SKILL.md` and load on demand. OpenCode V2 discovers this directory automatically and reloads configuration/skills after saves. If discovery is stale, use `/reload`, not a service restart.

Project commands: `/ui-audit`, `/ui-plan`, `/ui-review`. They do not change the model/provider or spawn agents.

## Product-specific design brief
- Help people understand what is downloading, whether it is progressing, what needs attention, and where the saved file is.
- Keep experienced-user controls available without making new users scan a wall of advanced settings.
- Make the host and extension recognizably related, but adapt interactions to each platform.
- Aim for a calm, precise Windows utility with deliberate personality. This is a proposed direction, not an owner-approved palette or layout.
- Preserve the app's existing light/dark/system theme mechanism, focus styling, status text + glyphs, expandable logs and native window behavior unless evidence calls for an improvement.
- Avoid decorative KPI panels, oversized marketing headers, fake progress, automatic queue reordering and gratuitous blur/glow. Do not add navigation destinations that have no real content.

## Process and approval boundary
1. **Baseline:** build the current source and inspect native rendering. Owner correction: `extension/docs/` images are outdated and must not be used as current-product evidence.
2. **Audit:** map common tasks, states, friction and existing strengths. Separate source facts from observed behavior and hypotheses.
3. **Direction:** compare two focused concepts using real content; recommend one with a compact token/component plan. Confirm major layout/identity changes with the owner.
4. **Implement:** one coherent slice, preserving the engine/security/persistence contracts. No framework migration or new features disguised as polish.
5. **Validate:** actual WPF before/after captures, build, relevant logic tests and manual critical-flow checks. Stop and report missing coverage.

## Acceptance checklist for future UI changes
- [ ] Current-source WPF baseline and after captures at the same size, DPI, theme and data state
- [ ] Empty, queued, active, indeterminate/processing, paused, completed, failed and cancelled presentations
- [ ] Long titles/paths, missing metadata, zero filter results, expanded logs and settings validation
- [ ] Light/dark/system appearance; Windows contrast themes and reduced-animation settings
- [ ] Default and minimum window size; Windows Snap layouts; 100%, 150%, 200% display scaling and enlarged text
- [ ] Keyboard-only access, visible/unobscured focus, dismissal and sensible focus restoration
- [ ] Accurate accessible names/roles/values and Narrator checks; critical updates without announcement spam
- [ ] Measured text/UI contrast on actual composited backgrounds, including hover/selected/focus states
- [ ] Stable scrolling and responsive controls with representative large queues; inspect virtualization
- [ ] No changes to cancel/remove/file retention, validation, history, engine updates or cookie handling without explicit scope

These are verification goals, **not passed checks**. A rendered synthetic-state preview does not establish live download or startup behavior.

## Initial source inventory (not a visual audit)
- `host/YTDLPHost/YTDLPHost.csproj`: .NET 8 WPF, MVVM toolkit; no need to assume React, WinUI or Compose.
- `App.xaml`, `MainWindow.xaml`, `Services/ThemeService.cs`: existing shared brush resources, native icon fonts, three theme modes and keyboard-focus styling.
- `MainWindow.xaml.cs`: filters, limits, menus/toasts and queue presentation behavior.
- `App.xaml.cs`, `ViewModels/MainViewModel.cs`: normal startup has protocol/history/engine side effects; inspect safely before launching.
- `extension/manifest.json`: Chromium Manifest V3; extension work remains deferred.
- Supplied folder has no `.git`; existing `bin/`/`obj/` do not prove the current source was built or tested.

Research references and standards/preference distinctions: `docs/UI-UX-SOURCES.md`.

Setup results, current-source native preview captures and explicit verification limits: `docs/UI-UX-BASELINE.md`.

Current desktop findings and proposed implementation boundary: `docs/UI-UX-AUDIT.md` and `docs/UI-UX-DIRECTION.md`. The proposal does not approve application changes by itself.

Owner-approved Direction A implementation, native before/after evidence and remaining verification: `docs/UI-UX-IMPLEMENTATION.md`.

Historical premium material research/proposal: `docs/UI-UX-PREMIUM-MATERIALS.md`. Research alone does not authorize glass/effect changes or a framework migration.

Subsequently owner-approved softer geometry/native-material slice, current WPF evidence and explicit remaining checks: `docs/UI-UX-PREMIUM-IMPLEMENTATION.md`. Final-service Mica visibility remains unconfirmed; the second temporary runtime/download/test files were removed, with an externally locked empty evidence folder remaining.

Owner-authorized temporary .NET 8 synthetic-state verification and completed runtime cleanup: `docs/UI-UX-NET8-VERIFICATION.md`. This does not validate normal startup, live downloads or DWM materials.

Owner-approved graphite/porcelain/blue palette, scoped native verification and retained evidence: `docs/UI-UX-PALETTE-IMPLEMENTATION.md`. Synthetic checks do not establish production-download behavior or full accessibility coverage.
