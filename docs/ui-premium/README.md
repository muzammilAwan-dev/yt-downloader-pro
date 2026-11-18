# Premium WPF evidence — 15 November 2026

Actual native WPF with synthetic data, not HTML and not normal app startup. See [implementation/limits](../UI-UX-PREMIUM-IMPLEMENTATION.md).

## Use these results

- `evidence/before/`: current-source, pre-premium solid baseline.
- `evidence/solid-final/`: authoritative seven-state solid after captures, brush-based text measurements, synthetic control checks and external UIA read.
- `evidence/product-final/`: authoritative successful guarded keyboard run, theme replacement fix, composited service/fallback and native lifecycle checks.
- `evidence/product-final-comparison/`: fresh light Mica/solid pair. Keyboard attempts in this later run were blocked by the foreground guard; do not infer keyboard passes from its action labels. A configured DWM type does not prove visible material.
- `evidence/verification-summary.json`: 68 unchanged binding expressions, contrast samples, unchanged 200-item realization and separation of successful/blocked keyboard runs.
- `cleanup-verification.json`: runtime/download/test files removed; **0 temporary files**, but one externally locked empty evidence directory and its parents remain.

Other `after*`, `product-verification*` and `material-probe*` folders are intermediate/historical evidence. In particular, initial `evidence/after/` bitmaps allowed native messages to re-enable transparency and are not valid matched solid comparisons. `product-verification-compiled` demonstrates the frozen-brush theme failure before the fix. Manual material-probe tint does not establish visible final-service Mica.

## Retained source

`before-source/` and `source-after/` preserve the exact compared sources. `preview/`, `material-probe/` and `product-verification/` retain the exact synthetic fixtures, selected-window capture guard and real-input harness. No ZIPs, runtimes or `bin/obj` were copied.

The project files are preserved **as executed**, including their original absolute host reference and temporary-path assumptions. They are evidence artifacts, not automatic portable tests. For a rerun, copy them into a new owner-approved temporary location, update only the host-reference/source-path plumbing, regenerate `SafeCompiledApplication.xaml` from the current app resources (change root class/converter assembly qualification only), and use a supported authorized runtime. Do not launch production `App` for synthetic inspection. Native capture/input must retain exact-window foreground/occlusion guards; refusal is a blocked check, not a reason to send global input.

The final solid fixture loads the built WPF window and source resources, restores/disposes materials for bitmap fidelity, and uses actual item view models without the real root download service. The interaction harness compiles those resources into a safe `Application` and references actual host controls/services; production startup/exit are never invoked. This does not verify normal startup, engine, settings, persistence, security or file actions.

## Integrity

`preservation-manifest.json` records SHA-256 and byte sizes for **219 exact retained files**, verified against originals before cleanup. Independently recheck them with:

```powershell
python docs/ui-premium/verification/verify-preservation.py
```

The README, cleanup record and independent verifier were added after the exact-copy manifest and are not included in its count. `verification/summarize-evidence.py` and `preserve-evidence.py` preserve the scripts used to summarize and retain the evidence; they do not download/install runtimes.
