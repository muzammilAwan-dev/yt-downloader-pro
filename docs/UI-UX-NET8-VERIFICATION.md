# Temporary .NET 8 desktop verification

15 November 2026. Owner explicitly authorized temporary .NET 8 setup for testing and removal afterward. **Completed and cleaned up.** No premium-material app edits, retargeting, system runtime installation or real download/startup test.

## Runtime isolation and cleanup

- Downloaded Microsoft's **8.0.34 win-x64 Core + Windows Desktop runtime ZIPs**, selected from the [official .NET 8 release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json). An SDK download was unnecessary: the existing SDK builds the unchanged .NET 8 target.
- Verified both archives against the published **SHA-512** values before extraction. The extracted `dotnet.exe` had a valid Microsoft .NET Authenticode signature. [Download evidence](ui-net8-verification/runtime-download-verification.json), [signature result](ui-net8-verification/dotnet-host-signature.json).
- Used only `C:\Users\Malku\AppData\Local\Temp\opencode\ytdlp-dotnet8-test-20261115\runtime\dotnet.exe`, by explicit path. No installer, admin elevation, registry/protocol registration or user/machine `PATH`/`DOTNET_ROOT` changes.
- The preview records **`.NET 8.0.34` and `PresentationFramework.dll` loaded from the isolated Windows Desktop 8.0.34 folder**. No major-version roll-forward used. The external UIA reader used the same runtime host.
- Saved evidence/source before cleanup and verified copied evidence with SHA-256. After all test processes exited, removed the exact newly created temporary runtime/download/test tree: **551 files, 264,328,937 bytes**. Original preview harness and repository evidence retained.
- Verified that the temporary directory is absent and the system runtime list is unchanged. [Cleanup record](ui-net8-verification/cleanup-verification.json). Windows Desktop 8 remains **not installed system-wide**, as intended.

**Maintenance note:** the fetched Microsoft release metadata marks .NET 8 end-of-support on **10 November 2026**. This test establishes compatibility with the current target; it does not restore support. A supported-runtime migration is a separately scoped maintenance decision, not a side effect of design work.

## What actually ran

The copied native preview loads the actual compiled `MainWindow` and item view models, with source-parsed `App.xaml` resources initialized from the real theme palettes. Synthetic root commands bypass engine/persistence/protocol/startup; Pause/Resume only change fixture state. Filter/count/footer content is fixture-controlled.

The temporary copy changed only evidence context, the external UIA runtime-host path and skipped physical-input calls. It did not change application code or the original harness. [Retained preview source](ui-net8-verification/preview-source/Program.cs).

Commands, with `<temporary-host>` denoting the explicit isolated `dotnet.exe`:

```text
dotnet build host/YTDLPHost.sln --configuration Debug --nologo
dotnet build tests/YTDLPHost.UiChecks/YTDLPHost.UiChecks.csproj --configuration Debug --nologo
dotnet build <temporary-preview-source>/NativePreview.csproj --configuration Debug --nologo
<temporary-host> tests/YTDLPHost.UiChecks/bin/Debug/net8.0-windows10.0.17763.0/YTDLPHost.UiChecks.dll
<temporary-host> <temporary-preview-source>/bin/Debug/net8.0-windows10.0.17763.0/NativePreview.dll <temporary-evidence>/captures
```

## Results and precise limits

| Check | Result / evidence | Limit |
| --- | --- | --- |
| Requested host solution build | **Pass: 0 warnings, 0 errors.** | Existing SDK; a successful build is not a UI/live-flow test. |
| Checks and preview builds | **Pass: 0 warnings, 0 errors.** [Checks](ui-net8-verification/checks-build.txt), [preview](ui-net8-verification/preview-build.txt) | No new dependency/framework. |
| Real-item presentation checks on .NET 8 | **Pass: all 174.** [Result](ui-net8-verification/presentation-checks-net8.txt) | Payload exclusion, titles, processing/metrics and presentation-action availability only. |
| Native WPF rendering on .NET 8 | **Seven captures opened and inspected**, listed below. Default light/dark fixture fits with recovery links visible; limits/minimum views scroll. | Synthetic data; client-area renders, not composited desktop screenshots. No DWM material. |
| Forward/reverse task traversal | **Pass, programmatic only:** all four task cards reached in dark/light default and light minimum/limits. [Summary](ui-net8-verification/verification-summary.json), detailed traversal files in `captures/` | Not physical Tab/Shift+Tab or focus-ring visual validation. |
| State/disclosure focus | **Pass, synthetic/programmatic:** Pause -> Resume focus; settings dismissal restores focus; details expansion retains focus. [Result](ui-net8-verification/captures/native-control-checks.json) | No live queue command executed. |
| Selected-window external UIA | **Inspected 98 elements**, reader exit 0. [Result](ui-net8-verification/captures/external-uia-result.txt), [tree](ui-net8-verification/captures/external-uia-window.json) | HWND/process-scoped read; no controls invoked, no Narrator. |
| Sampled ordinary-text contrast | **No sampled pair below 4.5:1:** 35 pairs in each default theme, 40 in minimum/limits. Minimum **5.11:1 dark, 4.93:1 light**. [Summary](ui-net8-verification/verification-summary.json) | Effective solid drawing brushes/alpha only; excludes icon fonts, hover/pressed/focus and glyph/pixel measurements. Not conformance certification. |
| Reduced-motion template branch | **Pass for fixture-injected `Tag=false`:** sweep collapsed while indeterminate remained true. | Windows animation setting was True; OS setting changes and toast behavior not tested. |
| Large queue realization | **Unchanged:** 200/200 generated containers. [Record](ui-net8-verification/captures/large-queue-containers.txt) | No virtualization/performance improvement or benchmark. |
| Cleanup | **Pass:** temporary directory absent; no matching test process before removal; system runtime list unchanged. | Only our temporary test tree removed. |

### Native captures

| Fixture | .NET 8 capture |
| --- | --- |
| Empty / dark | [01](ui-net8-verification/captures/01-empty-dark.png) |
| Queue / dark | [02](ui-net8-verification/captures/02-queue-dark.png) |
| Queue / light | [03](ui-net8-verification/captures/03-queue-light.png) |
| Limits / light | [04](ui-net8-verification/captures/04-limits-light.png) |
| Minimum + limits | [05](ui-net8-verification/captures/05-minimum-limits-light.png) |
| Queued / processing / live / cancelled | [06](ui-net8-verification/captures/06-queued-processing-live-cancelled-dark.png) |
| Expanded error and log | [07](ui-net8-verification/captures/07-expanded-error-and-log-dark.png) |

Defaults 980×700 DIPs; minimum 840×540. Host DPI 144; client-area output 96 DPI, native caption excluded. **Not a physical 150% screenshot.** [Runtime/capture context](ui-net8-verification/captures/preview-context.txt).

## Still not exercised

Normal product startup, `MainViewModel`, protocol/queue/history restoration, engine downloads/updates, live download/processing/retry, actual cancel/remove/file retention, cookie cleanup, clipboard/file commands, real settings validation, tray return and runtime light/dark/system theme switching. Parsed resource freezing still prevents this harness from establishing production theme-switch behavior.

Physical keyboard input was deliberately skipped in this rerun; no keys sent. Narrator, Windows contrast themes, actual reduced-motion changes, 100/150/200% physical display scaling, enlarged text, Snap and composited Mica/Acrylic remain not run.

The previous .NET 9 [Direction A delivery evidence](UI-UX-IMPLEMENTATION.md) remains the original matched before/after record. This additional .NET 8 run reduces the **synthetic presentation/runtime** gap only, not all production-validation gaps.
