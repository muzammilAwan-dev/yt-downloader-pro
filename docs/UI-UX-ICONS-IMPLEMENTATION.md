# Icon set + visible log action — implementation record

Scope requested by the owner: more professional/premium icons, a visible **Show log** action, a clearly visible **Cancel**. Built on the owner's current
"native satin + softer geometry" source (`docs/UI-UX-PREMIUM-IMPLEMENTATION.md`); no change to bindings, commands, resource names used by
`tests/YTDLPHost.UiChecks`, themes, engine, persistence or the extension.

## Changes

- **Icons:** the Segoe Fluent / MDL2 icon font (`Icon.*` strings + `IconFont` + `IconText`) is replaced by **Lucide** outline icons as WPF `Geometry`
  resources with the same `Icon.*` names (plus `Icon.CancelCircle`). Two reusable styles in `App.xaml`: `LucideIcon` (outline), `LucideIconSolid`
  (filled Play/Pause) and `LucideIconLarge` (thinner stroke for the empty-state icon). Icons take the surrounding `Foreground`, so hover/pressed/danger/
  theme changes recolor them without extra code. No font, package or runtime dependency is added. License: `THIRD-PARTY-NOTICES.md` (ISC + MIT for
  Feather-derived icons).
- **Card actions:** a visible **Show/hide log** button on every card. The "..." button is removed because everything in its menu is now visible on the
  card; the same menu still opens on right-click. **Cancel** uses a circled-X (`Icon.CancelCircle`, red on hover) so it no longer reads as a checkbox;
  **Remove** keeps the plain X.
- **Code:** `MainWindow.xaml.cs` menu icons use the same geometry (`MakeMenuItem`), `OnCardMoreClick` removed.

## Evidence and limits (read before accepting)

| Check | Result |
| --- | --- |
| Geometry converted from Lucide sources and rendered in a browser at 16/20/32 px, dark and light | Shapes correct. **This is not a WPF render.** |
| `App.xaml` / `MainWindow.xaml` well-formed XML; every `StaticResource` key and every `FindResource` key resolves; every event handler exists | Static check only |
| `dotnet build`, the 174 + 128 + palette checks, native captures, keyboard/UIA/Narrator, 100-200 % scaling | **Not run for this slice.** Run them before accepting. |

Expected things to look at in the first native capture: stroke weight at 100 % and 150 % scaling (icons are 14-20 px with a 2.2/24 stroke), the
Play/Pause fill, the circled-X Cancel hover/pressed colors in both themes, and that the Show log button reveals the log and scrolls the card into view.

```powershell
dotnet build host/YTDLPHost.sln --configuration Debug --nologo
dotnet build tests/YTDLPHost.UiChecks/YTDLPHost.UiChecks.csproj --configuration Debug --nologo
dotnet tests/YTDLPHost.UiChecks/bin/Debug/net8.0-windows10.0.17763.0/YTDLPHost.UiChecks.dll
# if only a newer .NET runtime is installed: dotnet --roll-forward Major <same dll>
```
