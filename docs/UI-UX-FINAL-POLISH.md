# Final polish — palette and progress treatment

Requested by the owner: dark must look properly dark; light must not be a flat white sheet; keep the richer download-progress look; make the palette
professional. This replaces the previous graphite/porcelain values; roles, brush keys, bindings and the theme mechanism are unchanged.

## Palette

**Dark — "Deep ink":** near-black window with cards lifted by one step, so depth comes from surface lightness, not shadows or glow.
**Light — "Mist":** a cool gray canvas with soft off-white cards, so it reads as a designed theme and not a white page. One accent hue in both.

| Role | Dark | Light |
| --- | --- | --- |
| Window (Background) | `#0E0F11` | `#E3E6EB` |
| Card (Surface) | `#16181B` | `#F5F6F8` |
| Raised / hover | `#1F2227` | `#E9ECF0` |
| Pressed | `#2C3138` | `#D9DEE5` |
| Border | `#262A31` | `#CBD1DA` |
| Strong border | `#424954` | `#A5AEBB` |
| Progress track | `#0A0B0D` | `#D3D8E0` |
| Log panel | `#0B0C0E` | `#EDEFF2` |
| Text | `#EEF0F3` | `#181C22` |
| Text, secondary | `#B4BAC5` | `#3E4754` |
| Text, muted | `#8D95A3` | `#515B69` |
| Accent | `#72A5F6` | `#1D58B0` |
| Accent hover | `#8DB6F9` | `#174A95` |
| Accent pressed | `#5E92E4` | `#123C7C` |
| Label on accent | `#0D1829` | `#FFFFFF` |
| Success | `#86C89A` | `#1F6B3E` |
| Warning | `#E6BA6A` | `#855400` |
| Error | `#F4939C` | `#A02A36` |

Status colors are muted and only ever mean status. Subtle/soft/border tints are the status color at fixed opacity (`ThemeService.cs` has the exact values).

## Audit (ported check, not the real run)

The token rules in `tests/YTDLPHost.UiChecks/PaletteChecks.cs` were re-implemented in a script and run against both palettes: no failures.
Weakest pairs: text 5.29:1 dark / 4.83:1 light (rule: 4.5), UI elements 5.26:1 dark / 4.49:1 light (rule: 3).
**This is not the real suite.** Run it (below) before accepting.

## Progress treatment

- Bar is 8 px with 4 px radius. The fill is a tonal gradient: the accent easing into a lighter tint of the same accent (`AccentColor` to `AccentLiftColor`).
  No second hue. The lift is 30 % toward white in dark and 14 % in light and is computed in `ThemeService.Apply`; both ends stay at least 3:1 on the track.
- Speed is the readout: 20 px semibold number with a small unit, ETA beside it, baseline-aligned. Paused/completed/error bars keep their solid status colors.
- `AccentLiftColor` is a new `Color` resource (not a brush), so the brush-key checks are unaffected.

## Not verified here

`dotnet build`, the UI checks, native captures in both themes, 100-200 % scaling. This environment has no .NET and cannot run WPF.

```powershell
dotnet build host/YTDLPHost.sln --configuration Debug --nologo
dotnet build tests/YTDLPHost.UiChecks/YTDLPHost.UiChecks.csproj --configuration Debug --nologo
dotnet tests/YTDLPHost.UiChecks/bin/Debug/net8.0-windows10.0.17763.0/YTDLPHost.UiChecks.dll
```
