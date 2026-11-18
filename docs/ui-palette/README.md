# Palette WPF evidence — 15 November 2026

Actual native WPF with synthetic data, not HTML or normal startup. See [implementation and limits](../UI-UX-PALETTE-IMPLEMENTATION.md).

- `evidence/before/`: fresh built pre-palette baseline; not the old repository screenshots.
- `evidence/after/`: matched seven-state final solid captures, effective-brush text measurements, programmatic control/peer inspection and external selected-window UIA read.
- `evidence/interaction-states/`: ten additional native bitmaps and actual painted-border/label brushes for injected hover/pressed/selected branches. **No physical pointer verification.** `IsMouseOver` reads false despite the injected hover branch painting; pressed reads true. No download commands executed.
- `evidence/compiled-native/`: resources compiled into the safe application; runtime theme replacement, guarded real keyboard input, native lifecycle, software solid fallback and composited selected-window captures. Historical `Production` labels mean production controls/services, **not production startup**. Configured Mica is not proof of visible material.
- `evidence/verification-summary.json`: matched dimensions, contrast samples, source invariants and successful keyboard traversal. Build/test authority: `host-final-build.txt`, `checks-final-build.txt`, `checks-final-results.txt`.
- `checks-initial-palette-results.txt` / `checks-palette-results.txt` retain the initial contrast rejection; they are not final results. Some PowerShell build logs are UTF-16; use `Get-Content` to read them.
- `cleanup-verification.json`: runtime/download/test files removed, **0 files remain**, system runtimes unchanged; externally locked empty `evidence/after` and `evidence/compiled-native` plus parents remain. `cleanup-initial-attempt.json` records the earlier partial removal, not the final cleanup result.

`before-source/` and `source-after/` retain compared application sources and final checks. `preview/`, `product-verification/`, `material-probe/` and `verification/` retain exact fixture/capture/bootstrap/resource-compilation/summary/preservation scripts. No ZIPs, runtimes, `bin` or `obj` are copied.

Fixtures are preserved **as executed**, with original absolute repository references and temporary paths. They are evidence artifacts, not portable automatic tests. Reruns require a new owner-approved isolated location, host/source-path plumbing updates and an authorized supported runtime. Compile source resources with only safe application root/converter assembly qualification changes; never launch production `App` for synthetic inspection. Keep exact-window input/occlusion guards. The solid fixture restores/disposes material handling to avoid transparent bitmap contamination; it cannot assess DWM.

## Integrity

`preservation-manifest.json` records SHA-256/byte sizes for **134 exact files**, verified against their originals before cleanup. Recheck retained files:

```powershell
python docs/ui-palette/verification/verify-preservation.py
```

This README, independent verifier, cleanup script and both cleanup records are added after the exact-copy manifest and are not included in its count. No usability study, full accessibility audit, performance improvement or live download success is established by this archive.
